# AI-ChatBox — Web App

A .NET 9 ASP.NET Core web app that connects to an Azure AI agent (using model 'gpt-4o-mini') in Microsoft Foundry and serves a browser-based chat interface.

<img width="757" height="361" alt="image" src="https://github.com/user-attachments/assets/6e3fccfc-42ef-48d5-8ae4-bccaf7c2f57c" />

<img width="1096" height="517" alt="image" src="https://github.com/user-attachments/assets/cf790b27-ede2-4db9-b9da-2eef3642e255" />

## Prerequisites
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- Access to the Microsoft Foundry (Azure AI Foundry) project at `aifoundaryplayground`

## Setup
Install Azure CLI if not already installed, and Authenticate with Azure before running:

az login

## Usage
dotnet restore
dotnet run

Then open `http://localhost:<port>` in your browser. The chat UI loads automatically from `wwwroot/index.html`.

## Architecture

| Layer | Description |
|-------|-------------|
| Backend |  ASP.NET Core minimal API. Registers `ProjectResponsesClient` as a singleton and exposes session-aware chat endpoints. |
| Frontend | Single-page chat UI served as static files. Sends fetch requests to `/api/chat`. |

### API
**`POST /api/chat`**
Request: json : { "message": "your message here", "sessionId": "optional-session-id" }
Response: json : { "reply": "agent response here", "sessionId": "session-id" }

**`DELETE /api/chat/memory/{sessionId}`**
Response: json : { "cleared": true|false }

## Configuration

The agent connection is hardcoded in `agent.cs` for this example:

| Setting       | Value |
|---------------|-------|
| Endpoint      | `https://aifoundaryplayground.services.ai.azure.com/api/projects/{{project name}}` |
| Agent name    | `{{agent name}}` |
| Agent version | `1` |

Authentication uses `DefaultAzureCredential` — no secrets in config. In production, this picks up managed identity or environment credentials automatically.

## Conversation Memory

- Memory is stored per chat session (session id generated in browser and persisted in localStorage).
- The backend keeps a rolling window of recent messages (up to 10 user/assistant turns) and includes that context in each new prompt.
- Memory is in-process only; restarting the app clears all session memory.
- Use `DELETE /api/chat/memory/{sessionId}` to clear memory for one session.

## Dependencies

| Package | Version |
|---------|---------|
| `Azure.AI.Projects` | 2.0.0-beta.2 |
| `Azure.AI.Projects.Agents` | 2.0.0-beta.1 |
| `Azure.Identity` | 1.19.0 |
| `OpenAI` | 2.9.1 |

## Related Project

The companion Razor Pages web app lives at `C:\repos\AI\ChatBox\ChatBox` — a .NET 8 ASP.NET Core app that connects to the same Azure AI agent.
