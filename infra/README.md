# Infrastructure — HelpdeskAI Azure Deployment

This folder contains **Bicep Infrastructure as Code** and deployment scripts to provision HelpdeskAI on Azure in 5–10 minutes.

---

## What Gets Deployed

| Resource | Type | Purpose | Tier/Size |
|----------|------|---------|-----------|
| **Azure OpenAI** | Cognitive Services | Hosts gpt-4.1 model for the AI agent | S0 SKU, 10 TPM |
| **Azure AI Search** | Search Service | Semantic search over KB documents (RAG) | Basic (1 replica, 1 partition) |

**Total monthly cost:** ~$150–200 (varies by usage)

---

## Prerequisites

Before you start, ensure you have:

1. **Azure CLI** — https://learn.microsoft.com/cli/azure/install-azure-cli
   ```bash
   az --version  # Must be 2.50.0 or later
   ```

2. **Bicep CLI** — automatically installed with Azure CLI 2.49+, or manually:
   ```bash
   az bicep install
   ```

3. **Azure subscription** with permission to:
   - Create resource groups
   - Create Cognitive Services (OpenAI) and Search resources
   - Assign RBAC roles
   - List/retrieve resource keys

4. **Azure OpenAI access** — request at https://aka.ms/oai/access if you don't have a quota for `gpt-4.1` or `gpt-4o`

5. **Logged in to Azure:**
   ```bash
   az login
   az account set --subscription "<Subscription ID or Name>"
   ```

---

## Deployment

### Quick Deploy

```bash
cd infra
.\deploy.ps1 -ResourceGroupName "rg-helpdeskai" -Location "swedencentral"
```

**Parameters:**
- `ResourceGroupName` — Azure resource group name (default: `rg-helpdeskaiapp-dev`)
- `Location` — Azure region (default: `swedencentral`); also supports `eastus`, `eastus2`, `westus2`, etc.
- `Environment` — `dev` | `staging` | `prod` (default: `dev`) — used for resource naming
- `BaseName` — resource name base (default: `helpdeskaiapp`)
- `WhatIf` — preview changes without deploying: `.\deploy.ps1 -WhatIf`
- `SkipSeedData` — skip KB seeding: `.\deploy.ps1 -SkipSeedData`

### What the Script Does

1. **Logs into Azure** — prompts `az login` if not already authenticated
2. **Creates resource group** — if it doesn't exist
3. **Deploys Bicep template** — provisions OpenAI + AI Search
4. **Creates AI Search index** — `helpdesk-kb` with semantic search config
5. **Seeds knowledge base** — uploads 5 IT articles from `seed-data.json`
6. **Generates config** — creates `src/HelpdeskAI.AgentHost/appsettings.Development.json` with credentials

**Typical runtime:** 5–10 minutes

### Dry-Run (Preview)

Before deploying, see what will be created:

```bash
.\deploy.ps1 -WhatIf
```

This runs an Azure deployment preview without provisioning any resources.

---

## File Reference

| File | Purpose |
|------|---------|
| `main.bicep` | Bicep infrastructure template (82 lines) |
| `deploy.ps1` | PowerShell deployment orchestration script |
| `seed-data.json` | Knowledge base articles (5 sample IT KB documents) |
| `setup-search.ps1` | Standalone KB index setup (if you need to re-create the index) |

---

## Bicep Template (`main.bicep`)

Minimal template defining two resources:

### Azure OpenAI Account

```bicep
resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${prefix}-openai-${uniqueSuffix}'
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    publicNetworkAccess: 'Enabled'
    customSubDomainName: '${prefix}-openai-${uniqueSuffix}'
  }
}

resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAiAccount
  name: 'gpt-4.1'
  sku: { name: 'GlobalStandard', capacity: 10 }  // TPM
  properties: {
    model: { format: 'OpenAI', name: 'gpt-4.1', version: '2025-04-14' }
  }
}
```

### Azure AI Search Service

```bicep
resource aiSearch 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: '${prefix}-search-${uniqueSuffix}'
  location: location
  sku: { name: 'basic' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'standard'
  }
}
```

---

## Knowledge Base (`seed-data.json`)

Contains 5 sample IT support articles. Each document structure:

```json
{
  "@search.action": "upload",
  "id": "KB-0001",
  "title": "How to reset password?",
  "category": "Access",
  "tags": ["password", "reset", "account"],
  "content": "Full article content describing the process..."
}
```

### Add Your Own Articles

Edit `seed-data.json` and add entries with:
- `id` — unique identifier (e.g., `KB-0006`)
- `title` — searchable title
- `category` — filter category
- `tags` — array of keywords
- `content` — full article text (the more detail, the better RAG results)

Then re-run the search setup:

```bash
.\setup-search.ps1 -SearchEndpoint "https://<search>.search.windows.net" `
                    -AdminKey "<search-admin-key>"
```

---

## Manual Deployment

If the automated script fails, deploy manually:

### 1. Create Resource Group

```bash
az group create --name "rg-helpdeskai" --location "swedencentral"
```

### 2. Deploy Bicep Template

```bash
az deployment group create \
  --resource-group "rg-helpdeskai" \
  --template-file main.bicep \
  --parameters \
    environment=dev \
    baseName=helpdeskaiapp \
    location=swedencentral
```

### 3. Get API Keys

```bash
# OpenAI admin key
az cognitiveservices account keys list \
  --resource-group "rg-helpdeskai" \
  --name "helpdeskaiapp-dev-openai-<hash>" \
  --query "key1" -o tsv

# Search admin key
az search admin-key show \
  --resource-group "rg-helpdeskai" \
  --service-name "helpdeskaiapp-dev-search-<hash>" \
  --query "primaryKey" -o tsv
```

### 4. Create Search Index

```bash
.\setup-search.ps1 -SearchEndpoint "https://<search>.search.windows.net" \
                    -AdminKey "<admin-key>"
```

### 5. Update appsettings.Development.json

Create `src/HelpdeskAI.AgentHost/appsettings.Development.json`:

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<resource>.openai.azure.com/",
    "ApiKey": "<api-key>",
    "ChatDeployment": "gpt-4.1"
  },
  "AzureAISearch": {
    "Endpoint": "https://<search>.search.windows.net",
    "ApiKey": "<admin-key>",
    "IndexName": "helpdesk-kb",
    "TopK": 3
  },
  "McpServer": {
    "Endpoint": "http://localhost:5100/mcp"
  },
  "Conversation": {
    "SummarisationThreshold": 20,
    "TailMessagesToKeep": 6,
    "ThreadTtl": "30.00:00:00"
  }
}
```

---

## Regional Availability

**Azure OpenAI `gpt-4.1` is available in:**
- `swedencentral` ✅ (recommended)
- `eastus2` ✅
- `westus2` ✅
- `eastus` ✓ (limited)

If you get a quota error, try a different region.

---

## Troubleshooting

### "Insufficient permissions"

**Error:** `AuthorizationFailed` when creating resources

**Fix:** You need `Contributor` + `User Access Administrator` roles on the subscription. Contact your Azure admin.

### "Bicep not found"

**Error:** `bicep: command not found`

**Fix:** Install Bicep CLI:
```bash
az bicep install
```

### "gpt-4.1 quota exceeded"

**Error:** `DeploymentFailed: Insufficient quota`

**Fix:** 
1. Try a different region (see [Regional Availability](#regional-availability))
2. Use `gpt-4o` instead — edit `main.bicep` line 64 to change `gpt-4.1` → `gpt-4o`
3. Request additional quota at https://aka.ms/oai/access

### "Deploy script exits with 1"

**Symptom:** PowerShell script terminates early

**Fix:**
1. Verify you're logged in: `az account show` should display your account
2. Set your subscription: `az account set --subscription "<id>"`
3. Check internet connectivity — Azure CLI needs to reach portal.azure.com

---

## Clean Up

To delete all deployed resources and stop incurring charges:

```bash
az group delete --name "rg-helpdeskai" --yes
```

This removes the resource group and all resources within it.

---

## Learn More

- **Bicep Docs:** https://learn.microsoft.com/azure/azure-resource-manager/bicep/
- **Azure OpenAI:** https://learn.microsoft.com/azure/ai-services/openai/
- **Azure AI Search:** https://learn.microsoft.com/azure/search/
- **Azure CLI Reference:** https://learn.microsoft.com/cli/azure/reference-index
