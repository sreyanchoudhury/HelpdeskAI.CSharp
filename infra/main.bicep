// HelpdeskAI Infrastructure
// Bicep template for Azure resource deployment
//
// Resources provisioned:
//   - Azure AI Foundry account with OpenAI-compatible endpoint
//   - Azure AI Search
//   - Cosmos DB
//   - Blob Storage
//   - Document Intelligence

@description('Deployment environment')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Base name for all resources (lowercase, no spaces)')
param baseName string = 'helpdeskaiapp'

@description('Azure region for all resources')
param location string = 'swedencentral'

@description('Primary chat deployment name for v1')
param primaryChatDeploymentName string = 'gpt-5.3-chat'

@description('Primary chat model name for v1')
param primaryChatModelName string = 'gpt-5.3-chat'

@description('Primary chat model version for v1')
param primaryChatModelVersion string = '2025-04-14'

@description('V2 workflow chat model version')
param gpt52ChatModelVersion string = '2025-04-14'

@description('Embedding model version')
param embeddingModelVersion string = '1'

@description('Primary chat deployment capacity (TPM)')
param primaryChatCapacity int = 10

@description('V2 workflow deployment name')
param v2ChatDeploymentName string = 'gpt-5.2-chat'

@description('V2 workflow model name')
param v2ChatModelName string = 'gpt-5.2-chat'

@description('V2 workflow deployment capacity (TPM)')
param gpt52ChatCapacity int = 10

@description('Embedding deployment capacity (TPM)')
param embeddingCapacity int = 30

@description('Eval scorer deployment name')
param evalScorerDeploymentName string = 'gpt-4.1-mini-eval'

@description('Eval scorer model name')
param evalScorerModelName string = 'gpt-4.1-mini'

@description('Eval scorer model version')
param evalScorerModelVersion string = '2025-04-14'

@description('Eval scorer deployment capacity (TPM)')
param evalScorerCapacity int = 10

@description('Blob container name for attachments')
param blobContainerName string = 'helpdesk-attachments'

@description('Default Foundry project name')
param foundryProjectName string = 'default'

// Derived Names
var prefix = '${baseName}-${environment}'
var uniqueSuffix = uniqueString(resourceGroup().id, baseName)
var storageAccountBase = replace(toLower('${baseName}${environment}${uniqueSuffix}st'), '-', '')
var storageAccountName = length(storageAccountBase) < 3 ? '${storageAccountBase}stg' : take(storageAccountBase, 24)

var names = {
  foundry:  '${prefix}-fndry-${uniqueSuffix}'
  aiSearch: '${prefix}-search-${uniqueSuffix}'
  docIntel: '${prefix}-docintel-${uniqueSuffix}'
}

// Foundry resource in the Cognitive Services namespace.
// This is intentionally not a hub-based Azure ML deployment.
resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: names.foundry
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  kind: 'AIServices'
  sku: { name: 'S0' }
  properties: {
    allowProjectManagement: true
    defaultProject: foundryProjectName
    publicNetworkAccess: 'Enabled'
    customSubDomainName: names.foundry
  }
}

// Foundry project as a child of the Cognitive Services account.
resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: foundryAccount
  name: foundryProjectName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    description: 'Default project for HelpdeskAI shared infrastructure.'
    displayName: 'HelpdeskAI'
  }
}

// Primary chat deployment for v1
resource primaryChatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: primaryChatDeploymentName
  dependsOn: [
    foundryProject
  ]
  sku: {
    name: 'GlobalStandard'
    capacity: primaryChatCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: primaryChatModelName
      version: primaryChatModelVersion
    }
  }
}

// Secondary workflow deployment for v2 validation
resource gpt52ChatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: v2ChatDeploymentName
  dependsOn: [
    primaryChatDeployment
  ]
  sku: {
    name: 'GlobalStandard'
    capacity: gpt52ChatCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: v2ChatModelName
      version: gpt52ChatModelVersion
    }
  }
}

// Embeddings for RAG and dynamic tool selection
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: 'text-embedding-3-small'
  dependsOn: [
    gpt52ChatDeployment
  ]
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      version: embeddingModelVersion
    }
  }
}

// Dedicated scorer deployment for AI evaluations.
// This stays separate from the runtime chat pair because the evaluation SDK uses
// deterministic scoring settings that are not reliably supported by the GPT-5 chat deployments.
resource evalScorerDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = {
  parent: foundryAccount
  name: evalScorerDeploymentName
  dependsOn: [
    embeddingDeployment
  ]
  sku: {
    name: 'GlobalStandard'
    capacity: evalScorerCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: evalScorerModelName
      version: evalScorerModelVersion
    }
  }
}

// Azure AI Search
resource aiSearch 'Microsoft.Search/searchServices@2024-06-01-preview' = {
  name: names.aiSearch
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

// Blob Storage for attachment persistence
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    encryption: {
      keySource: 'Microsoft.Storage'
      services: {
        blob: {
          enabled: true
        }
      }
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource attachmentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: blobContainerName
  properties: {
    publicAccess: 'None'
  }
}

// Document Intelligence for OCR and structured extraction
resource docIntelligence 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: names.docIntel
  location: location
  kind: 'FormRecognizer'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: names.docIntel
    publicNetworkAccess: 'Enabled'
  }
}

// Cosmos DB Account for Ticket Persistence (Serverless — no idle cost)
resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: '${prefix}-cosmos-${uniqueSuffix}'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [{ name: 'EnableServerless' }]
    locations: [{ locationName: location, failoverPriority: 0 }]
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
    minimalTlsVersion: 'Tls12'
  }
}

resource helpdeskDb 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  name: 'helpdeskdb'
  parent: cosmosAccount
  properties: {
    resource: { id: 'helpdeskdb' }
  }
}

// Partition key: /id (INC-NNNN) — point reads are always single-partition
resource ticketsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  name: 'tickets'
  parent: helpdeskDb
  properties: {
    resource: {
      id: 'tickets'
      partitionKey: { paths: ['/id'], kind: 'Hash' }
      // Default indexing policy covers all fields; ORDER BY createdAt works without composite index
    }
  }
}

// Outputs
output resourceGroupName string = resourceGroup().name
output openAiEndpoint string = foundryAccount.properties.endpoint
output openAiAccountId string = foundryAccount.id
output foundryProjectName string = foundryProject.name
output foundryResourceKind string = foundryAccount.kind
output evalScorerDeploymentNameOutput string = evalScorerDeployment.name
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
output aiSearchResourceId string = aiSearch.id
output cosmosEndpoint    string = cosmosAccount.properties.documentEndpoint
output cosmosAccountName string = cosmosAccount.name  // key retrieved in deploy.ps1 via az cosmosdb keys list
output storageAccountName string = storageAccount.name
output blobContainerNameOutput string = blobContainerName
output docIntelligenceEndpoint string = docIntelligence.properties.endpoint
output docIntelligenceAccountName string = docIntelligence.name
