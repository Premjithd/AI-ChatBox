# AIChatBox — Web App

A .NET 9 ASP.NET Core web app that connects to an Azure AI agent in Microsoft Foundry and serves a browser-based chat interface.

<img width="757" height="361" alt="image" src="https://github.com/user-attachments/assets/6e3fccfc-42ef-48d5-8ae4-bccaf7c2f57c" />

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- Access to the Azure AI Foundry / Microsoft Foundry project at `aifoundaryplayground`

## Setup

Authenticate with Azure before running:

```shell
az login
```

## Usage

```shell
dotnet restore
dotnet run
```

Then open `http://localhost:<port>` in your browser. The chat UI loads automatically from `wwwroot/index.html`.

## Architecture

| Layer | File | Description |
|-------|------|-------------|
| Backend | `agent.cs` | ASP.NET Core minimal API. Registers `ProjectResponsesClient` as a singleton and exposes `POST /api/chat`. |
| Frontend | `wwwroot/index.html` | Single-page chat UI served as static files. Sends fetch requests to `/api/chat`. |

### API

**`POST /api/chat`**

Request:
```json
{ "message": "your message here" }
```

Response:
```json
{ "reply": "agent response here" }
```

## Configuration

The agent connection is hardcoded in `agent.cs`:

| Setting | Value |
|---|---|
| Endpoint | `https://aifoundaryplayground.services.ai.azure.com/api/projects/project-AI` |
| Agent name | `MyChatbox` |
| Agent version | `1` |

Authentication uses `DefaultAzureCredential` — no secrets in config. In production, this picks up managed identity or environment credentials automatically.

## Dependencies

| Package | Version |
|---|---|
| `Azure.AI.Projects` | 2.0.0-beta.2 |
| `Azure.AI.Projects.Agents` | 2.0.0-beta.1 |
| `Azure.Identity` | 1.19.0 |
| `OpenAI` | 2.9.1 |

## Related Project

The companion Razor Pages web app lives at `C:\repos\AI\ChatBox\ChatBox` — a .NET 8 ASP.NET Core app that connects to the same Azure AI agent.
