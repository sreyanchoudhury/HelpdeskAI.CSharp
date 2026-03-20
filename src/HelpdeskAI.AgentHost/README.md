# HelpdeskAI.AgentHost

AgentHost is the ASP.NET Core backend that hosts the AG-UI agent runtime and all browser-facing backend APIs.

## Responsibilities

- Expose the AG-UI streaming endpoint at `/agent`
- Validate Microsoft Entra bearer tokens
- Derive user context from claims for the agent prompt
- Inject Redis-backed long-term user profile memory and remembered preferences into the agent context
- Inject Azure AI Search context before model calls
- Connect to MCP tools exposed by `HelpdeskAI.McpServer`
- Handle attachment upload and download flows
- Proxy ticket and knowledge base operations for the frontend

## Important Endpoints

- `POST /agent`: authenticated AG-UI streaming endpoint
- `GET /agent/usage`: authenticated usage lookup
- `GET /agent/info`: anonymous service metadata
- `GET /healthz`: health endpoint
- `GET /api/kb/search`: authenticated KB search
- `GET /api/tickets`: authenticated ticket proxy
- `POST /api/attachments`: authenticated upload
- `GET /api/attachments/{*blobName}`: authenticated download
- `POST /agent/eval`: non-production-only eval endpoint

## Configuration

Example `appsettings.Development.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<your-openai-resource>.openai.azure.com/",
    "ApiKey": "<openai-key>",
    "ChatDeployment": "gpt-4.1-mini",
    "EmbeddingDeployment": "text-embedding-3-small"
  },
  "AzureAISearch": {
    "Endpoint": "https://<your-search>.search.windows.net",
    "ApiKey": "<search-key>",
    "IndexName": "helpdesk-kb",
    "TopK": 3
  },
  "EntraAuth": {
    "TenantId": "<entra-tenant-id>",
    "ClientId": "<entra-app-client-id>",
    "Audience": "api://<entra-app-client-id>",
    "Authority": "https://login.microsoftonline.com/<entra-tenant-id>/v2.0"
  },
  "LongTermMemory": {
    "ProfileTtl": "90.00:00:00"
  },
  "McpServer": {
    "Endpoint": "http://localhost:5100/mcp"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

Local development can still target Azure-hosted dependencies directly if you prefer not to run everything locally.

## Local Run

```powershell
cd src/HelpdeskAI.AgentHost
dotnet run
```

Default local URL: `http://localhost:5200`

## Azure Notes

- AgentHost expects an Entra v2 access token.
- Audience should match `api://<clientId>` unless explicitly overridden.
- Container app settings are wired through `infra/app-deploy/apps.bicep`.
- Attachment downloads should go through the frontend proxy or authenticated AgentHost route, not raw blob URLs.
- Long-term profile memory is stored in Redis under a user-keyed namespace with a configurable TTL.
- Turn-level telemetry now logs repeated tool invocations and captures the latest user message in the `/agent` request scope.

## Current Caveats

- Long-term memory currently stores profile facts and simple remembered preferences only; richer memory categories are still future work.
