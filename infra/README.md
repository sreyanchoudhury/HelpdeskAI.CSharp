# HelpdeskAI Infrastructure

This folder contains the Azure infrastructure and deployment assets for HelpdeskAI.

## What Lives Here

- `main.bicep`: core Azure resources such as Azure OpenAI and Azure AI Search
- `deploy.ps1`: provisioning script for the base infrastructure and local config generation
- `setup-search.ps1`: standalone Azure AI Search index setup and seed script
- `seed-data.json`: knowledge base seed data
- `app-deploy/apps.bicep`: container app stack
- `app-deploy/apps.bicepparam`: `azd` environment variable mappings for app deployment

## Deployment Model

There are two deployment scopes:

- Infrastructure provisioning: creates or updates Azure resources
- App deployment: builds and deploys the application containers

If infrastructure such as Cosmos DB or Entra app settings is already in place, app-only deployment is usually enough.

## Entra Requirements

The app registration used by the frontend and AgentHost should have:

- sign-in audience appropriate for your tenant
- redirect URIs for Azure and local frontend callbacks
- API exposure at `api://<clientId>`
- access token version `2`
- delegated scope such as `access_as_user`

The active `azd` environment should provide:

- `AZURE_AD_TENANT_ID`
- `AZURE_AD_CLIENT_ID`
- `AZURE_AD_CLIENT_SECRET`
- `NEXTAUTH_SECRET`

## Knowledge Base Index

Provisioning and search scripts expect the `helpdesk-kb` index to include:

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

`indexedAt` is sortable and is used by latest-article browse flows.

## Typical Commands

### Provision infrastructure

```powershell
cd infra
.\deploy.ps1 -ResourceGroupName "rg-helpdeskaiapp-dev" -Location "swedencentral"
```

### App-only deploy

```powershell
azd deploy
```

### Recreate or reseed the search index

```powershell
cd infra
.\setup-search.ps1 -SearchEndpoint "https://<search>.search.windows.net" -AdminKey "<search-admin-key>"
```

## Notes

- Local development can still point directly at Azure-hosted services; a separate local sandbox is not required.
- App deployment reads Entra and NextAuth settings from the current `azd` environment.
- If you reprovision container apps, follow with `azd deploy` so the live images are restored after environment changes.
- AgentHost long-term profile memory currently uses Redis and does not require a new Azure resource beyond the existing Redis container app.
- The same Redis instance now also stores simple long-term user preferences and turn-level workflow telemetry support data.
