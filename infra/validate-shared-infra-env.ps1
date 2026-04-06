Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredKeys = @(
    'AZURE_OPENAI_ENDPOINT',
    'AZURE_OPENAI_API_KEY',
    'AZURE_OPENAI_CHAT_DEPLOYMENT',
    'AZURE_OPENAI_EMBEDDING_DEPLOYMENT',
    'AZURE_AI_SEARCH_ENDPOINT',
    'AZURE_AI_SEARCH_API_KEY',
    'AZURE_BLOB_CONNECTION_STRING',
    'AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT',
    'AZURE_DOCUMENT_INTELLIGENCE_KEY'
)

$optionalKeys = @(
    'AZURE_OPENAI_CHAT_DEPLOYMENT_V2',
    'AZURE_COSMOS_DB_ENDPOINT',
    'AZURE_COSMOS_DB_PRIMARY_KEY'
)

$envValues = @{}
azd env get-values | ForEach-Object {
    if ($_ -match '^(?<key>[A-Za-z0-9_]+)="?(?<value>.*)"?$') {
        $envValues[$matches['key']] = $matches['value'].Trim('"')
    }
}

$missing = @()
foreach ($key in $requiredKeys) {
    if (-not $envValues.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envValues[$key])) {
        $missing += $key
    }
}

if ($missing.Count -gt 0) {
    Write-Host ''
    Write-Host 'Shared infrastructure environment validation failed.' -ForegroundColor Red
    Write-Host 'azd is configured to deploy only the app layer in this repo.' -ForegroundColor Yellow
    Write-Host 'Populate the shared infrastructure and Foundry/OpenAI-compatible settings in the active azd environment before running azd provision or azd deploy.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host 'Missing required azd env values:' -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  - $_" }
    Write-Host ''
    Write-Host 'Optional value:' -ForegroundColor Cyan
    $optionalKeys | ForEach-Object { Write-Host "  - $_" }
    Write-Host ''
    exit 1
}

Write-Host 'Shared infrastructure environment validation passed.' -ForegroundColor Green