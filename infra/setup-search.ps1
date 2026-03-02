<#
.SYNOPSIS
    Creates the helpdesk-kb index in Azure AI Search and seeds it with sample KB articles.

.PARAMETER SearchEndpoint
    Your Azure AI Search endpoint, e.g. https://your-search.search.windows.net

.PARAMETER AdminKey
    Your Azure AI Search Admin key (portal: Search resource > Keys).

.EXAMPLE
    .\setup-search.ps1 -SearchEndpoint "https://your-search.search.windows.net" -AdminKey "abc123..."
#>

param(
    [Parameter(Mandatory)][string]$SearchEndpoint,
    [Parameter(Mandatory)][string]$AdminKey
)

$ErrorActionPreference = "Stop"

$endpoint   = $SearchEndpoint.TrimEnd('/')
$apiVersion = "2024-07-01"
$headers    = @{
    "api-key"      = $AdminKey
    "Content-Type" = "application/json"
}

# ---------------------------------------------------------------------------
# 1. Create (or replace) the index
# ---------------------------------------------------------------------------
Write-Host "Creating index 'helpdesk-kb'..."

$schema = @{
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

# PUT is idempotent: creates the index if it does not exist, updates schema if it does.
Invoke-RestMethod `
    -Uri     "$endpoint/indexes/helpdesk-kb?api-version=$apiVersion" `
    -Method  PUT `
    -Headers $headers `
    -Body    $schema | Out-Null

Write-Host "  OK -- index ready." -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Seed documents
# ---------------------------------------------------------------------------
Write-Host "Seeding knowledge base..."

$seedPath = Join-Path $PSScriptRoot "seed-data.json"
if (-not (Test-Path $seedPath)) {
    Write-Error "seed-data.json not found at: $seedPath"
    exit 1
}

$documents = Get-Content $seedPath -Raw

Invoke-RestMethod `
    -Uri     "$endpoint/indexes/helpdesk-kb/docs/index?api-version=$apiVersion" `
    -Method  POST `
    -Headers $headers `
    -Body    $documents | Out-Null

$count = ($documents | ConvertFrom-Json).value.Count
Write-Host "  OK -- $count articles uploaded." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "All done. Add these values to appsettings.Development.json:" -ForegroundColor Cyan
Write-Host "  `"AzureAISearch`": {"
Write-Host "    `"Endpoint`":  `"$endpoint`","
Write-Host "    `"ApiKey`":    `"<your-admin-key>`","
Write-Host "    `"IndexName`": `"helpdesk-kb`","
Write-Host "    `"TopK`":      3"
Write-Host "  }"
