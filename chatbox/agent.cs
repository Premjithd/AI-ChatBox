#pragma warning disable OPENAI001
#pragma warning disable SKEXP0020

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using OpenAI.Responses;
using System.Collections.Concurrent;
using System.Text;

const string endpoint = "https://aifoundaryplayground.services.ai.azure.com/api/projects/project-AI";
const string agentName = "MyChatbox";
const string agentVersion = "1";

var builder = WebApplication.CreateBuilder(args);

// Shared Azure AI Foundry client — used by both the agent and embedding generator
builder.Services.AddSingleton(_ =>
    new AIProjectClient(endpoint: new Uri(endpoint), tokenProvider: new DefaultAzureCredential()));

builder.Services.AddSingleton(sp =>
{
    var projectClient = sp.GetRequiredService<AIProjectClient>();
    return projectClient.OpenAI.GetProjectResponsesClientForAgent(new AgentReference(agentName, agentVersion));
});

builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new LocalEmbeddingGeneratorAdapter());

builder.Services.AddSingleton<InMemoryVectorStore>();
builder.Services.AddSingleton<KnowledgeService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Index text files from the knowledge/ directory at startup
var knowledgeService = app.Services.GetRequiredService<KnowledgeService>();
await knowledgeService.EnsureCollectionAsync();

string knowledgeDir = Path.Combine(AppContext.BaseDirectory, "knowledge");
if (Directory.Exists(knowledgeDir))
{
    int maxChunkLength = app.Configuration.GetValue<int>("Knowledge:MaxChunkLength", 500);
    foreach (string file in Directory.GetFiles(knowledgeDir, "*.txt"))
    {
        string text = await File.ReadAllTextAsync(file);
        foreach (string chunk in TextChunker.ChunkByParagraph(text, maxChunkLength))
            await knowledgeService.IndexAsync(chunk, Path.GetFileName(file));
    }
}

const int maxTurnsToRemember = 10;
ConcurrentDictionary<string, ConversationMemory> memoryBySession = new();

app.MapPost("/api/chat", async (ChatRequest req, ProjectResponsesClient responseClient, KnowledgeService ks, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "Message cannot be empty." });

    string sessionId = string.IsNullOrWhiteSpace(req.SessionId)
        ? Guid.NewGuid().ToString("N")
        : req.SessionId.Trim();

    ConversationMemory conversation = memoryBySession.GetOrAdd(sessionId, _ => new ConversationMemory());

    int topK = config.GetValue<int>("Knowledge:TopK", 3);
    IReadOnlyList<KnowledgeChunk> knowledgeChunks = await ks.SearchAsync(req.Message, topK);

    string prompt;
    lock (conversation.SyncRoot)
    {
        prompt = ChatMemoryHelpers.BuildPrompt(conversation.Turns, req.Message, maxTurnsToRemember, knowledgeChunks);
    }

    ResponseResult response = await Task.Run(() => responseClient.CreateResponseAsync(prompt));
    string reply = response.GetOutputText();

    lock (conversation.SyncRoot)
    {
        conversation.Turns.Add(new ChatTurn("user", req.Message));
        conversation.Turns.Add(new ChatTurn("assistant", reply));
        ChatMemoryHelpers.TrimToMostRecentTurns(conversation.Turns, maxTurnsToRemember * 2);
        conversation.UpdatedAtUtc = DateTime.UtcNow;
    }

    return Results.Ok(new { reply, sessionId });
});

app.MapDelete("/api/chat/memory/{sessionId}", (string sessionId) =>
{
    if (string.IsNullOrWhiteSpace(sessionId))
        return Results.BadRequest(new { error = "SessionId cannot be empty." });

    bool removed = memoryBySession.TryRemove(sessionId.Trim(), out _);
    return Results.Ok(new { cleared = removed });
});

// Index a document into the vector store at runtime
app.MapPost("/api/knowledge", async (KnowledgeRequest req, KnowledgeService ks, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(req.Text))
        return Results.BadRequest(new { error = "Text cannot be empty." });

    int maxChunkLength = config.GetValue<int>("Knowledge:MaxChunkLength", 500);
    string source = string.IsNullOrWhiteSpace(req.Source) ? "api" : req.Source;
    foreach (string chunk in TextChunker.ChunkByParagraph(req.Text, maxChunkLength))
        await ks.IndexAsync(chunk, source);

    return Results.Ok(new { indexed = true, source });
});

app.Run();

record ChatRequest(string Message, string? SessionId);
record KnowledgeRequest(string Text, string? Source);
record ChatTurn(string Role, string Content);

sealed class ConversationMemory
{
    public object SyncRoot { get; } = new();
    public List<ChatTurn> Turns { get; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

static class TextChunker
{
    public static IEnumerable<string> ChunkByParagraph(string text, int maxLength)
    {
        string[] paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
        StringBuilder current = new();

        foreach (string paragraph in paragraphs)
        {
            string trimmed = paragraph.Trim();
            if (trimmed.Length == 0) continue;

            if (current.Length + trimmed.Length > maxLength && current.Length > 0)
            {
                yield return current.ToString().Trim();
                current.Clear();
            }

            if (current.Length > 0) current.AppendLine();
            current.Append(trimmed);
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }
}

static class ChatMemoryHelpers
{
    public static string BuildPrompt(
        List<ChatTurn> allTurns,
        string latestUserMessage,
        int maxTurnsToRemember,
        IReadOnlyList<KnowledgeChunk>? knowledgeChunks = null)
    {
        StringBuilder sb = new();

        if (knowledgeChunks is { Count: > 0 })
        {
            sb.AppendLine("Use the following relevant knowledge when answering:");
            sb.AppendLine();
            foreach (KnowledgeChunk chunk in knowledgeChunks)
            {
                sb.AppendLine($"[Source: {chunk.Source}]");
                sb.AppendLine(chunk.Text);
                sb.AppendLine();
            }
        }

        if (allTurns.Count > 0)
        {
            int keepCount = Math.Min(allTurns.Count, maxTurnsToRemember * 2);
            List<ChatTurn> contextTurns = allTurns.Skip(allTurns.Count - keepCount).ToList();

            sb.AppendLine("Conversation history:");
            sb.AppendLine();
            foreach (ChatTurn turn in contextTurns)
            {
                string speaker = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                sb.AppendLine($"{speaker}: {turn.Content}");
            }
            sb.AppendLine();
        }

        if (sb.Length > 0)
        {
            sb.AppendLine("Latest user message:");
            sb.AppendLine(latestUserMessage);
            return sb.ToString();
        }

        return latestUserMessage;
    }

    public static void TrimToMostRecentTurns(List<ChatTurn> turns, int maxItems)
    {
        if (turns.Count <= maxItems) return;
        turns.RemoveRange(0, turns.Count - maxItems);
    }
}
