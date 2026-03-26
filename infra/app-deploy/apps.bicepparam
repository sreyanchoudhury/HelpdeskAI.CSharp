using './apps.bicep'

// Fixed values
param environment = 'dev'
param baseName    = 'helpdeskaiapp'

// Region — set via: azd env set AZURE_LOCATION "swedencentral"
param location = readEnvironmentVariable('AZURE_LOCATION', 'swedencentral')

// ─── Azure OpenAI ─────────────────────────────────────────────────────────────
// azd env set AZURE_OPENAI_ENDPOINT          "https://<your-resource>.openai.azure.com/"
// azd env set AZURE_OPENAI_API_KEY           "<key>"
// azd env set AZURE_OPENAI_CHAT_DEPLOYMENT   "gpt-4.1-mini"       (optional, has default)
// azd env set AZURE_OPENAI_EMBEDDING_DEPLOYMENT "text-embedding-3-small" (optional)
param openAiEndpoint            = readEnvironmentVariable('AZURE_OPENAI_ENDPOINT', '')
param openAiApiKey              = readEnvironmentVariable('AZURE_OPENAI_API_KEY', '')
param openAiChatDeployment      = readEnvironmentVariable('AZURE_OPENAI_CHAT_DEPLOYMENT', 'gpt-4o')
param openAiChatDeploymentV2   = readEnvironmentVariable('AZURE_OPENAI_CHAT_DEPLOYMENT_V2', '')
param openAiEmbeddingDeployment = readEnvironmentVariable('AZURE_OPENAI_EMBEDDING_DEPLOYMENT', 'text-embedding-3-small')

// ─── Azure AI Search ──────────────────────────────────────────────────────────
// azd env set AZURE_AI_SEARCH_ENDPOINT  "https://<your-resource>.search.windows.net"
// azd env set AZURE_AI_SEARCH_API_KEY   "<key>"
param aiSearchEndpoint = readEnvironmentVariable('AZURE_AI_SEARCH_ENDPOINT', '')
param aiSearchApiKey   = readEnvironmentVariable('AZURE_AI_SEARCH_API_KEY', '')

// ─── Azure Blob Storage ───────────────────────────────────────────────────────
// azd env set AZURE_BLOB_CONNECTION_STRING  "DefaultEndpointsProtocol=https;AccountName=..."
param blobConnectionString = readEnvironmentVariable('AZURE_BLOB_CONNECTION_STRING', '')

// ─── Document Intelligence ────────────────────────────────────────────────────
// azd env set AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT  "https://<your-resource>.cognitiveservices.azure.com/"
// azd env set AZURE_DOCUMENT_INTELLIGENCE_KEY       "<key>"
param docIntelligenceEndpoint = readEnvironmentVariable('AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT', '')
param docIntelligenceKey      = readEnvironmentVariable('AZURE_DOCUMENT_INTELLIGENCE_KEY', '')

// ─── Microsoft Entra ID / NextAuth ────────────────────────────────────────────
// azd env set AZURE_AD_TENANT_ID       "<tenant-id>"
// azd env set AZURE_AD_CLIENT_ID       "<app-registration-client-id>"
// azd env set AZURE_AD_CLIENT_SECRET   "<app-registration-client-secret>"
// azd env set NEXTAUTH_SECRET          "<random-32-plus-char-secret>"
param azureAdTenantId     = readEnvironmentVariable('AZURE_AD_TENANT_ID', '')
param azureAdClientId     = readEnvironmentVariable('AZURE_AD_CLIENT_ID', '')
param azureAdClientSecret = readEnvironmentVariable('AZURE_AD_CLIENT_SECRET', '')
param nextAuthSecret      = readEnvironmentVariable('NEXTAUTH_SECRET', '')
