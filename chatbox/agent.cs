#pragma warning disable OPENAI001

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using OpenAI.Responses;
using System.Collections.Concurrent;
using System.Text;

const string endpoint = "https://aifoundaryplayground.services.ai.azure.com/api/projects/project-AI";
const string agentName = "MyChatbox";
const string agentVersion = "1";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(_ =>
{
    AIProjectClient projectClient = new(endpoint: new Uri(endpoint), tokenProvider: new DefaultAzureCredential());
    AgentReference agentReference = new(name: agentName, version: agentVersion);
    return projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

const int maxTurnsToRemember = 10;
ConcurrentDictionary<string, ConversationMemory> memoryBySession = new();

app.MapPost("/api/chat", async (ChatRequest req, ProjectResponsesClient responseClient) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "Message cannot be empty." });

    string sessionId = string.IsNullOrWhiteSpace(req.SessionId)
        ? Guid.NewGuid().ToString("N")
        : req.SessionId.Trim();

    ConversationMemory conversation = memoryBySession.GetOrAdd(sessionId, _ => new ConversationMemory());

    string prompt;
    lock (conversation.SyncRoot)
    {
        prompt = ChatMemoryHelpers.BuildPrompt(conversation.Turns, req.Message, maxTurnsToRemember);
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

app.Run();

record ChatRequest(string Message, string? SessionId);

record ChatTurn(string Role, string Content);

sealed class ConversationMemory
{
    public object SyncRoot { get; } = new();
    public List<ChatTurn> Turns { get; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

static class ChatMemoryHelpers
{
    public static string BuildPrompt(List<ChatTurn> allTurns, string latestUserMessage, int maxTurnsToRemember)
    {
        if (allTurns.Count == 0)
            return latestUserMessage;

        int keepCount = Math.Min(allTurns.Count, maxTurnsToRemember * 2);
        List<ChatTurn> contextTurns = allTurns.Skip(allTurns.Count - keepCount).ToList();

        StringBuilder promptBuilder = new();
        promptBuilder.AppendLine("Use the following chat context when answering.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Conversation:");

        foreach (ChatTurn turn in contextTurns)
        {
            string speaker = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
            promptBuilder.AppendLine($"{speaker}: {turn.Content}");
        }

        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Latest user message:");
        promptBuilder.AppendLine(latestUserMessage);

        return promptBuilder.ToString();
    }

    public static void TrimToMostRecentTurns(List<ChatTurn> turns, int maxItems)
    {
        if (turns.Count <= maxItems)
            return;

        int removeCount = turns.Count - maxItems;
        turns.RemoveRange(0, removeCount);
    }
}
