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

// Outputs
output resourceGroupName string = resourceGroup().name
output openAiEndpoint string = openAiAccount.properties.endpoint
output openAiAccountId string = openAiAccount.id
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
output aiSearchResourceId string = aiSearch.id
