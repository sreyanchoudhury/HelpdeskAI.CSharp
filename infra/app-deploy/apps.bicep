// HelpdeskAI App Deployment
// Provisions all application resources for container-based deployment.
//
// Resources: Log Analytics Workspace, Application Insights, Container Registry,
//            Container Apps Environment, Redis, McpServer, AgentHost, Frontend.
//
// Deploy with:
//   azd provision   (reads params from apps.bicepparam via azd env variables)
//   azd deploy      (builds Docker images → ACR → updates Container Apps)

// ─── Parameters ───────────────────────────────────────────────────────────────

@description('Deployment environment label')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Azure region (defaults to resource group location)')
param location string = resourceGroup().location

@description('Base name for all resources — lowercase letters only, no hyphens, max 14 chars')
@maxLength(14)
param baseName string = 'helpdeskaiapp'

// Azure OpenAI
@secure()
@description('Azure OpenAI endpoint URL')
param openAiEndpoint string

@secure()
@description('Azure OpenAI API key')
param openAiApiKey string

@description('Chat model deployment name')
param openAiChatDeployment string = 'gpt-4.1-mini'

@description('Embedding model deployment name')
param openAiEmbeddingDeployment string = 'text-embedding-3-small'

// Azure AI Search
@secure()
@description('Azure AI Search endpoint URL')
param aiSearchEndpoint string

@secure()
@description('Azure AI Search API key')
param aiSearchApiKey string

@description('AI Search index name')
param aiSearchIndexName string = 'helpdesk-kb'

// Azure Blob Storage
@secure()
@description('Azure Blob Storage connection string')
param blobConnectionString string

@description('Blob container name for attachments')
param blobContainerName string = 'helpdesk-attachments'

// Document Intelligence
@secure()
@description('Document Intelligence endpoint URL')
param docIntelligenceEndpoint string

@secure()
@description('Document Intelligence API key')
param docIntelligenceKey string

// ─── Derived Names ────────────────────────────────────────────────────────────

var suffix = take(uniqueString(resourceGroup().id, baseName), 8)
var prefix = '${baseName}-${environment}'
// ACR name: alphanumeric only (no hyphens), globally unique
var acrName = '${baseName}${environment}${suffix}'

var names = {
  logAnalytics:     '${prefix}-logs-${suffix}'
  appInsights:      '${prefix}-ai-${suffix}'
  containerAppsEnv: '${prefix}-env-${suffix}'
  redis:            '${prefix}-redis'
  mcpServer:        '${prefix}-mcpserver'
  agentHost:        '${prefix}-agenthost'
  frontend:         '${prefix}-frontend'
}

// ─── Log Analytics Workspace ──────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logAnalytics
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ─── Application Insights (workspace-based) ───────────────────────────────────

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appInsights
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ─── Azure Container Registry ─────────────────────────────────────────────────

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: true
  }
}

// ─── Container Apps Environment ───────────────────────────────────────────────

resource caEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: names.containerAppsEnv
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// ─── Redis Container App (internal TCP :6379) ─────────────────────────────────
// Note: ephemeral storage — chat history is cleared on Container App restart.
//       Acceptable for demo; upgrade to Azure Cache for Redis for persistence.

resource redisApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.redis
  location: location
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: false
        transport: 'tcp'
        targetPort: 6379
        exposedPort: 6379
      }
    }
    template: {
      containers: [
        {
          name: 'redis'
          image: 'redis:7-alpine'
          command: ['redis-server']
          args: ['--appendonly', 'yes', '--maxmemory', '256mb', '--maxmemory-policy', 'allkeys-lru']
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
}

// ─── McpServer Container App (internal HTTP) ──────────────────────────────────
// Not internet-facing. Only called by AgentHost within the same environment.

resource mcpServerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.mcpServer
  location: location
  tags: { 'azd-service-name': 'mcpserver' }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password',       value: acr.listCredentials().passwords[0].value }
        { name: 'ai-search-endpoint', value: aiSearchEndpoint }
        { name: 'ai-search-key',      value: aiSearchApiKey }
        { name: 'appinsights-cs',     value: appInsights.properties.ConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'mcpserver'
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          env: [
            { name: 'ASPNETCORE_URLS',                        value: 'http://+:8080' }
            { name: 'AzureAISearch__Endpoint',                secretRef: 'ai-search-endpoint' }
            { name: 'AzureAISearch__ApiKey',                  secretRef: 'ai-search-key' }
            { name: 'AzureAISearch__IndexName',               value: aiSearchIndexName }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',  secretRef: 'appinsights-cs' }
          ]
          resources: {
            cpu: json('0.75')
            memory: '1.5Gi'
          }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

// ─── AgentHost Container App (external HTTPS) ─────────────────────────────────

resource agentHostApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.agentHost
  location: location
  tags: { 'azd-service-name': 'agenthost' }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password',          value: acr.listCredentials().passwords[0].value }
        { name: 'openai-endpoint',        value: openAiEndpoint }
        { name: 'openai-api-key',         value: openAiApiKey }
        { name: 'ai-search-endpoint',     value: aiSearchEndpoint }
        { name: 'ai-search-key',          value: aiSearchApiKey }
        { name: 'blob-connection-string', value: blobConnectionString }
        { name: 'doc-intel-endpoint',     value: docIntelligenceEndpoint }
        { name: 'doc-intel-key',          value: docIntelligenceKey }
        { name: 'appinsights-cs',         value: appInsights.properties.ConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'agenthost'
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          env: [
            { name: 'ASPNETCORE_URLS',                        value: 'http://+:8080' }
            // AllowedOrigins=* tells the ASP.NET Core CORS middleware to allow any origin
            { name: 'AllowedOrigins',                         value: '*' }
            { name: 'AzureOpenAI__Endpoint',                  secretRef: 'openai-endpoint' }
            { name: 'AzureOpenAI__ApiKey',                    secretRef: 'openai-api-key' }
            { name: 'AzureOpenAI__ChatDeployment',            value: openAiChatDeployment }
            { name: 'AzureOpenAI__EmbeddingDeployment',       value: openAiEmbeddingDeployment }
            { name: 'AzureAISearch__Endpoint',                secretRef: 'ai-search-endpoint' }
            { name: 'AzureAISearch__ApiKey',                  secretRef: 'ai-search-key' }
            { name: 'AzureAISearch__IndexName',               value: aiSearchIndexName }
            { name: 'DynamicTools__TopK',                     value: '5' }
            // Internal hostnames within a Container Apps Environment:
            //   HTTP apps  → http://<appname>          (short name, no TLS)
            //   TCP apps   → <appname>:<port>          (short name resolves within the environment)
            { name: 'McpServer__Endpoint',                    value: 'http://${names.mcpServer}/mcp' }
            { name: 'ConnectionStrings__Redis',               value: '${names.redis}:6379,abortConnect=false,connectTimeout=5000' }
            { name: 'AzureBlobStorage__ConnectionString',     secretRef: 'blob-connection-string' }
            { name: 'AzureBlobStorage__ContainerName',        value: blobContainerName }
            { name: 'DocumentIntelligence__Endpoint',         secretRef: 'doc-intel-endpoint' }
            { name: 'DocumentIntelligence__Key',              secretRef: 'doc-intel-key' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',  secretRef: 'appinsights-cs' }
          ]
          resources: {
            cpu: json('1.5')
            memory: '3Gi'
          }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ─── Frontend Container App (external HTTPS) ──────────────────────────────────

resource frontendApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.frontend
  location: location
  tags: { 'azd-service-name': 'frontend' }
  properties: {
    managedEnvironmentId: caEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 3000
        transport: 'http'
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        { name: 'acr-password', value: acr.listCredentials().passwords[0].value }
      ]
    }
    template: {
      containers: [
        {
          name: 'frontend'
          image: 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
          env: [
            // Points to the AgentHost external FQDN — set from the deployed resource's ingress FQDN.
            // NOTE: AGENT_URL must NOT be in next.config.ts env block or it gets baked at build time.
            //       These are read at runtime by Next.js API routes via process.env.
            { name: 'AGENT_URL',      value: 'https://${agentHostApp.properties.configuration.ingress.fqdn}/agent' }
            { name: 'AGENT_BASE_URL', value: 'https://${agentHostApp.properties.configuration.ingress.fqdn}' }
            // Internal hostname — reachable from other apps within the same Container Apps environment.
            { name: 'MCP_URL',        value: 'http://${names.mcpServer}' }
            { name: 'NODE_ENV',       value: 'production' }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 2 }
    }
  }
}

// ─── Outputs ──────────────────────────────────────────────────────────────────

output acrLoginServer string = acr.properties.loginServer
output agentHostUrl   string = 'https://${agentHostApp.properties.configuration.ingress.fqdn}'
output frontendUrl    string = 'https://${frontendApp.properties.configuration.ingress.fqdn}'
