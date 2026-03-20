# HelpdeskAI.McpServer

McpServer exposes the tool surface used by AgentHost.

## Responsibilities

- Ticket management tools backed by Cosmos DB
- System status and incident tools for demo and operational flows
- Knowledge base tooling for article search and indexing
- Internal REST ticket listing used by AgentHost

## Tool Groups

### Tickets

- `create_ticket`
- `get_ticket`
- `search_tickets`
- `update_ticket_status`
- `add_ticket_comment`
- `assign_ticket`

### System Status

- `get_system_status`
- `get_active_incidents`
- `check_impact_for_team`

### Knowledge Base

- `search_kb_articles`
- `index_kb_article`

## Endpoints

- `GET/POST /mcp`: MCP transport
- `GET /tickets`: internal REST endpoint used by AgentHost
- `GET /healthz`: health endpoint

## Configuration

Example configuration:

```json
{
  "AzureAISearch": {
    "Endpoint": "https://<your-search>.search.windows.net",
    "ApiKey": "<search-key>",
    "IndexName": "helpdesk-kb",
    "TopK": 3
  },
  "CosmosDb": {
    "Endpoint": "https://<your-cosmos>.documents.azure.com:443/",
    "PrimaryKey": "<cosmos-key>",
    "DatabaseName": "helpdeskdb",
    "ContainerName": "tickets"
  }
}
```

## Knowledge Base Index Shape

The runtime and provisioning scripts expect the Azure AI Search index to contain:

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

`indexedAt` is sortable and is used by the latest-articles browse flow.

## Local Run

```powershell
cd src/HelpdeskAI.McpServer
dotnet run
```

Default local URL: `http://localhost:5100`

## Azure Notes

- Cosmos DB provisioning and container app wiring are managed through the infrastructure templates.
- AgentHost is the intended consumer of the MCP and `/tickets` endpoints.
- Local development can target Azure-hosted Cosmos DB and Azure AI Search if desired.
