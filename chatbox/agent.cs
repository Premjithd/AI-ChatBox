#pragma warning disable OPENAI001

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using OpenAI.Responses;

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

app.MapPost("/api/chat", async (ChatRequest req, ProjectResponsesClient responseClient) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "Message cannot be empty." });

    ResponseResult response = await Task.Run(() => responseClient.CreateResponse(req.Message));
    return Results.Ok(new { reply = response.GetOutputText() });
});

app.Run();

record ChatRequest(string Message);
