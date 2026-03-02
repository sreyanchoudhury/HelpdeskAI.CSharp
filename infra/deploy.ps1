#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys all HelpdeskAI Azure resources and seeds the AI Search index.

.DESCRIPTION
    End-to-end deployment script that:
    1. Creates an Azure Resource Group
    2. Deploys all resources via Bicep template
    3. Seeds the Azure AI Search index with KB documents
    4. Outputs configuration values for local development

.PARAMETER ResourceGroupName
    Name of the Azure Resource Group to create (default: rg-helpdeskaiapp-dev)

.PARAMETER Location
    Azure region for deployment (default: swedencentral)

.PARAMETER Environment
    Deployment environment: dev, staging, prod (default: dev)

.PARAMETER BaseName
    Base name for resources (default: helpdeskaiapp)

.EXAMPLE
    .\deploy.ps1 -ResourceGroupName "rg-helpdeskaiapp-dev" -Location "swedencentral"
    .\deploy.ps1 -WhatIf
#>
param(
    [string]$ResourceGroupName = "rg-helpdeskaiapp-dev",
    [string]$Location         = "swedencentral",
    [string]$Environment      = "dev",
    [string]$BaseName         = "helpdeskaiapp",
    [switch]$SkipSeedData,
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Output helpers
function Write-Step($msg)  { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)    { Write-Host "  [OK]   $msg" -ForegroundColor Green }
function Write-Warn($msg)  { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err($msg)   { Write-Host "  [ERR]  $msg" -ForegroundColor Red }

# Preflight checks
Write-Step "Preflight checks"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Err "Azure CLI not found. Install from: https://aka.ms/installazurecli"
    exit 1
}

$account = az account show --query "{name:name, id:id, tenant:tenantId}" -o json | ConvertFrom-Json
if (-not $account) {
    Write-Warn "Not logged in. Running 'az login'..."
    az login
    $account = az account show --query "{name:name, id:id, tenant:tenantId}" -o json | ConvertFrom-Json
}

Write-Ok "Logged in as subscription: $($account.name) ($($account.id))"

# Resource Group
Write-Step "Creating resource group: $ResourceGroupName in $Location"

if ($WhatIf) {
    Write-Warn "[WhatIf] Would create resource group: $ResourceGroupName"
} else {
    az group create `
        --name $ResourceGroupName `
        --location $Location `
        --tags "Project=HelpdeskAI" "Environment=$Environment" "ManagedBy=Bicep" `
        --output none
    Write-Ok "Resource group ready: $ResourceGroupName"
}

# Deploy Bicep Template
Write-Step "Deploying Azure resources via Bicep template..."

$deploymentName = "helpdeskaiapp-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$bicepPath = Join-Path $PSScriptRoot "main.bicep"

if (-not (Test-Path $bicepPath)) {
    Write-Err "Bicep template not found at: $bicepPath"
    exit 1
}

# Build deployment command with conditional what-if
$deployAction = "create"
if ($WhatIf) {
    $deployAction = "what-if"
}

$deployCmd = @(
    "deployment", "group", $deployAction,
    "--name", $deploymentName,
    "--resource-group", $ResourceGroupName,
    "--template-file", $bicepPath,
    "--parameters", "environment=$Environment", "baseName=$BaseName", "location=$Location",
    "--output", "json"
)

$deployOutput = (az @deployCmd) | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Err "Bicep deployment failed!"
    exit $LASTEXITCODE
}

if ($WhatIf) {
    Write-Ok "WhatIf complete. No resources were created."
    exit 0
}

# Extract outputs
Write-Step "Extracting deployment outputs..."

$outputs = $deployOutput.properties.outputs

$openAiEndpoint   = $outputs.openAiEndpoint.value
$aiSearchEndpoint = $outputs.aiSearchEndpoint.value
$openAiAccountId  = $outputs.openAiAccountId.value
$aiSearchResourceId = $outputs.aiSearchResourceId.value

Write-Ok "Azure OpenAI endpoint: $openAiEndpoint"
Write-Ok "AI Search endpoint:    $aiSearchEndpoint"

# Retrieve API keys using Azure CLI
Write-Step "Retrieving API keys from Azure resources..."

$openAiApiKey = az cognitiveservices account keys list --name (($openAiAccountId -split '/')[-1]) --resource-group $ResourceGroupName --query "key1" -o tsv
$aiSearchApiKey = az search admin-key show --resource-group $ResourceGroupName --service-name (($aiSearchResourceId -split '/')[-1]) --query "primaryKey" -o tsv

Write-Ok "API keys retrieved"

# Create AI Search Index + Seed Data
if (-not $SkipSeedData) {
    Write-Step "Creating Azure AI Search index: helpdesk-kb"

    $indexSchema = @{
        name   = "helpdesk-kb"
        fields = @(
            @{ name = "id";       type = "Edm.String";             key = $true;  retrievable = $true;  searchable = $false }
            @{ name = "title";    type = "Edm.String";             key = $false; retrievable = $true;  searchable = $true;  analyzer = "en.microsoft" }
            @{ name = "content";  type = "Edm.String";             key = $false; retrievable = $true;  searchable = $true;  analyzer = "en.microsoft" }
            @{ name = "category"; type = "Edm.String";             key = $false; retrievable = $true;  searchable = $true;  filterable = $true }
            @{ name = "tags";     type = "Collection(Edm.String)"; key = $false; retrievable = $true;  searchable = $true;  filterable = $true }
        )
        semantic = @{
            configurations = @(
                @{
                    name = "helpdesk-semantic-config"
                    prioritizedFields = @{
                        titleField = @{ fieldName = "title" }
                        prioritizedContentFields = @(@{ fieldName = "content" })
                        prioritizedKeywordsFields = @(@{ fieldName = "category" }, @{ fieldName = "tags" })
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 10

    Invoke-RestMethod `
        -Uri     "$aiSearchEndpoint/indexes/helpdesk-kb?api-version=2024-07-01" `
        -Method  PUT `
        -Headers @{ "api-key" = $aiSearchApiKey; "Content-Type" = "application/json" } `
        -Body    $indexSchema | Out-Null

    Write-Ok "Index 'helpdesk-kb' created"

    # Seed KB documents
    Write-Step "Seeding knowledge base articles..."

    $seedPath = Join-Path $PSScriptRoot "seed-data.json"
    if (-not (Test-Path $seedPath)) {
        Write-Err "seed-data.json not found at: $seedPath"
        exit 1
    }

    $documents = Get-Content $seedPath -Raw

    Invoke-RestMethod `
        -Uri     "$aiSearchEndpoint/indexes/helpdesk-kb/docs/index?api-version=2024-07-01" `
        -Method  POST `
        -Headers @{ "api-key" = $aiSearchApiKey; "Content-Type" = "application/json" } `
        -Body    $documents | Out-Null

    $count = ($documents | ConvertFrom-Json).value.Count
    Write-Ok "$count KB articles indexed in Azure AI Search"
}

# Generate appsettings.Development.json
Write-Step "Generating local development configuration..."

$agentSettings = @{
    AzureOpenAI = @{
        Endpoint       = $openAiEndpoint
        ApiKey         = $openAiApiKey
        ChatDeployment = "gpt-4.1"
    }
    AzureAISearch = @{
        Endpoint  = $aiSearchEndpoint
        ApiKey    = $aiSearchApiKey
        IndexName = "helpdesk-kb"
        TopK      = 3
    }
    Conversation = @{
        SummarisationThreshold = 20
        TailMessagesToKeep     = 6
        ThreadTtl              = "30.00:00:00"
    }
} | ConvertTo-Json -Depth 10

$settingsPath = Join-Path $PSScriptRoot ".." "src" "HelpdeskAI.AgentHost" "appsettings.Development.json"
$agentSettings | Out-File -FilePath $settingsPath -Encoding UTF8

Write-Ok "Generated: $settingsPath"
Write-Warn "This file contains API keys -- it is in .gitignore. Never commit secrets!"

# Summary
Write-Host ""
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host "  HelpdeskAI Deployment Complete!" -ForegroundColor Magenta
Write-Host "================================================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Resource Group : $ResourceGroupName"
Write-Host "  Location       : $Location"
Write-Host "  AI Search      : $aiSearchEndpoint"
Write-Host ""
Write-Host "  Local Development:" -ForegroundColor Cyan
Write-Host "  cd src/HelpdeskAI.McpServer  && dotnet run"
Write-Host "  cd src/HelpdeskAI.AgentHost  && dotnet run"
Write-Host "  cd src/HelpdeskAI.Frontend   && npm install && npm run dev"
Write-Host ""

