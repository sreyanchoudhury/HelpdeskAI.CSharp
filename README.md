# HelpdeskAI

HelpdeskAI is an enterprise helpdesk assistant built on .NET 10, Next.js, Azure OpenAI, Azure AI Search, Azure Blob Storage, Azure Cosmos DB, and Microsoft Entra ID.

The solution has three main apps:

- `src/HelpdeskAI.Frontend`: Next.js frontend, NextAuth session handling, AG-UI client, upload/download proxies.
- `src/HelpdeskAI.AgentHost`: ASP.NET Core agent host, AG-UI endpoint, bearer token validation, RAG injection, attachment handling, ticket proxying.
- `src/HelpdeskAI.McpServer`: MCP server exposing ticket, system status, and knowledge base tools.

## Current State

- Tickets are persisted in Cosmos DB.
- The frontend authenticates users with Microsoft Entra ID.
- Frontend server routes forward Entra bearer tokens to AgentHost.
- AgentHost validates tokens and derives user context from claims.
- NextAuth refreshes Entra access tokens so server-side proxy routes stay authenticated across longer sessions.
- Attachments are downloaded through authenticated frontend and AgentHost proxy routes instead of direct blob links.
- The Azure AI Search knowledge base uses `id`, `title`, `content`, `category`, `tags`, and `indexedAt`.
- AgentHost now keeps a Redis-backed long-term profile memory keyed by signed-in user email.
- Simple “remember that ...” preferences are now persisted into long-term memory and reused across sessions.

## Local And Azure

The codebase supports both local development and Azure deployment.

- Local development can use locally running dependencies.
- Local development can also point directly to Azure-hosted dependencies and endpoints.
- A separate local sandbox environment is not required if you want to work against Azure resources.
- Azure deployment is handled through the infrastructure under [`infra/`](infra/README.md).

## Local Setup

### Frontend

Create `src/HelpdeskAI.Frontend/.env.local`:

```env
AGENT_URL=http://localhost:5200/agent
AGENT_BASE_URL=http://localhost:5200
MCP_URL=http://localhost:5100
NEXTAUTH_URL=http://localhost:3000
NEXTAUTH_SECRET=<random-secret>
AZURE_AD_CLIENT_ID=<entra-app-client-id>
AZURE_AD_CLIENT_SECRET=<entra-app-client-secret>
AZURE_AD_TENANT_ID=<entra-tenant-id>
AZURE_AD_API_SCOPE=api://<entra-app-client-id>/access_as_user
```

You can swap `AGENT_URL`, `AGENT_BASE_URL`, and `MCP_URL` to Azure-hosted endpoints if you do not want to run those services locally.

### AgentHost

Create `src/HelpdeskAI.AgentHost/appsettings.Development.json`:

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
  "McpServer": {
    "Endpoint": "http://localhost:5100/mcp"
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

### Run Locally

```powershell
# Terminal 1
cd src/HelpdeskAI.McpServer
dotnet run

# Terminal 2
cd src/HelpdeskAI.AgentHost
dotnet run

# Terminal 3
cd src/HelpdeskAI.Frontend
npm install
npm run dev
```

Open `http://localhost:3000`.

## Azure Setup

- Infrastructure and app deployment guidance lives in [`infra/README.md`](infra/README.md).
- The Entra app registration must expose `api://<clientId>` and a delegated scope such as `access_as_user`.
- AgentHost expects Entra token version 2 and validates the bearer token audience.
- App deployment reads `AZURE_AD_*` and `NEXTAUTH_SECRET` from the active `azd` environment.

## Knowledge Base Index

The `helpdesk-kb` index shape used by provisioning and runtime code is:

```json
{
  "id": "KB-0001",
  "title": "How to reset password",
  "content": "Full article text",
  "category": "Access",
  "tags": ["password", "reset", "account"],
  "indexedAt": "2026-01-15T00:00:00Z"
}
```

`indexedAt` is important because AgentHost uses it to browse the latest knowledge base entries.

## Key Paths

- [`infra/README.md`](infra/README.md)
- [`src/HelpdeskAI.Frontend/README.md`](src/HelpdeskAI.Frontend/README.md)
- [`src/HelpdeskAI.AgentHost/README.md`](src/HelpdeskAI.AgentHost/README.md)
- [`src/HelpdeskAI.McpServer/README.md`](src/HelpdeskAI.McpServer/README.md)
- [`docs/model-compatibility.md`](docs/model-compatibility.md)

## Known Follow-Up Items

- Phase 3 long-term memory currently covers profile memory and simple remembered preferences only. Resolved-ticket memory and KB-gap tracking are still future work.
- Render cards are still model-mediated through the current `_renderAction` flow and can remain occasionally inconsistent until a more deterministic render architecture is introduced.
