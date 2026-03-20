// HelpdeskAI Infrastructure - Minimal Configuration
// Bicep template for Azure resource deployment
//
// Resources provisioned:
//   - Azure OpenAI (GPT-4.1 model)
//   - Azure AI Search (Basic tier for semantic search)

@description('Deployment environment')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Base name for all resources (lowercase, no spaces)')
param baseName string = 'helpdeskaiapp'

@description('Azure region for all resources')
param location string = 'swedencentral'

@description('GPT-4.1 model version')
param gptModelVersion string = '2025-04-14'

@description('Deployment capacity (TPM)')
param gptCapacity int = 10

// Derived Names
var prefix = '${baseName}-${environment}'
var uniqueSuffix = uniqueString(resourceGroup().id, baseName)

var names = {
  openAi:   '${prefix}-openai-${uniqueSuffix}'
  aiSearch: '${prefix}-search-${uniqueSuffix}'
}

// Azure OpenAI Service
resource openAiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: names.openAi
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    publicNetworkAccess: 'Enabled'
    customSubDomainName: names.openAi
  }
}

// GPT-4.1 Model Deployment
resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAiAccount
  name: 'gpt-4.1'
  sku: {
    name: 'GlobalStandard'
    capacity: gptCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: gptModelVersion
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
output openAiEndpoint string = openAiAccount.properties.endpoint
output openAiAccountId string = openAiAccount.id
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
output aiSearchResourceId string = aiSearch.id
output cosmosEndpoint    string = cosmosAccount.properties.documentEndpoint
output cosmosAccountName string = cosmosAccount.name  // key retrieved in deploy.ps1 via az cosmosdb keys list
