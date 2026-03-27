<#
.SYNOPSIS
    Removes demo-created AI Search and Cosmos DB data while preserving repository seed data.

.DESCRIPTION
    This script is intended for regression-cycle cleanup. It preserves:
      - KB articles whose IDs are present in infra/seed-data.json
      - Seeded ticket documents whose seq is <= SeedTicketMaxSeq

    It deletes:
      - Agent-indexed or manually added KB documents not present in seed-data.json
      - Cosmos ticket documents above the configured seed threshold
#>

param(
    [Parameter(Mandatory)][string]$SearchEndpoint,
    [Parameter(Mandatory)][string]$SearchAdminKey,
    [Parameter(Mandatory)][string]$CosmosEndpoint,
    [Parameter(Mandatory)][string]$CosmosPrimaryKey,
    [string]$SearchIndexName = "helpdesk-kb",
    [string]$CosmosDatabaseName = "helpdeskdb",
    [string]$CosmosContainerName = "tickets",
    [int]$SeedTicketMaxSeq = 1013
)

$ErrorActionPreference = "Stop"

$searchApiVersion = "2024-07-01"
$searchBase = $SearchEndpoint.TrimEnd("/")
$searchHeaders = @{
    "api-key" = $SearchAdminKey
    "Content-Type" = "application/json"
}

$seedPath = Join-Path $PSScriptRoot "seed-data.json"
if (-not (Test-Path $seedPath)) {
    throw "seed-data.json not found at $seedPath"
}

$seedDoc = Get-Content $seedPath -Raw -Encoding UTF8 | ConvertFrom-Json
$seedKbIds = @($seedDoc.value | ForEach-Object { $_.id })
$seedKbLookup = @{}
foreach ($id in $seedKbIds) {
    $seedKbLookup[$id] = $true
}

Write-Host "Loaded $($seedKbIds.Count) seeded KB IDs from seed-data.json" -ForegroundColor Cyan

$searchBody = @{
    search = "*"
    select = "id"
    top    = 1000
    count  = $true
} | ConvertTo-Json

try {
    $searchResult = Invoke-RestMethod `
        -Method POST `
        -Uri "$searchBase/indexes/$SearchIndexName/docs/search?api-version=$searchApiVersion" `
        -Headers $searchHeaders `
        -Body $searchBody

    $kbDeletes = @()
    foreach ($doc in $searchResult.value) {
        $id = [string]$doc.id
        if (-not $seedKbLookup.ContainsKey($id)) {
            $kbDeletes += @{
                "@search.action" = "delete"
                id = $id
            }
        }
    }

    if ($kbDeletes.Count -eq 0) {
        Write-Host "AI Search cleanup: nothing to delete." -ForegroundColor Green
    }
    else {
        Write-Host "Deleting $($kbDeletes.Count) non-seed AI Search document(s)..." -ForegroundColor Yellow
        $deleteBody = @{ value = $kbDeletes } | ConvertTo-Json -Depth 6
        Invoke-RestMethod `
            -Method POST `
            -Uri "$searchBase/indexes/$SearchIndexName/docs/index?api-version=$searchApiVersion" `
            -Headers $searchHeaders `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($deleteBody)) | Out-Null
        Write-Host "AI Search cleanup complete." -ForegroundColor Green
    }
}
catch {
    Write-Warning "AI Search cleanup skipped: $($_.Exception.Message)"
}

Add-Type -AssemblyName "System.Web"

function New-CosmosAuthHeader {
    param(
        [string]$Verb,
        [string]$ResourceType,
        [string]$ResourceLink,
        [string]$Date,
        [string]$Key
    )

    $keyBytes = [Convert]::FromBase64String($Key)
    $payload = "{0}`n{1}`n{2}`n{3}`n`n" -f $Verb.ToLowerInvariant(), $ResourceType.ToLowerInvariant(), $ResourceLink, $Date.ToLowerInvariant()
    $hmac = [System.Security.Cryptography.HMACSHA256]::new($keyBytes)
    try {
        $hash = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($payload))
    }
    finally {
        $hmac.Dispose()
    }

    $signature = [Convert]::ToBase64String($hash)
    return [System.Web.HttpUtility]::UrlEncode("type=master&ver=1.0&sig=$signature")
}

function Invoke-CosmosRest {
    param(
        [string]$Verb,
        [string]$ResourceType,
        [string]$ResourceLink,
        [string]$Path,
        [string]$Key,
        [string]$Body = "",
        [hashtable]$ExtraHeaders
    )

    $date = [DateTime]::UtcNow.ToString("r")
    $auth = New-CosmosAuthHeader -Verb $Verb -ResourceType $ResourceType -ResourceLink $ResourceLink -Date $date -Key $Key

    $headers = @{
        "x-ms-date" = $date
        "x-ms-version" = "2018-12-31"
        "authorization" = $auth
    }

    if ($ExtraHeaders) {
        foreach ($keyName in $ExtraHeaders.Keys) {
            $headers[$keyName] = $ExtraHeaders[$keyName]
        }
    }

    $params = @{
        Method = $Verb
        Uri = "$($CosmosEndpoint.TrimEnd('/'))/$Path"
        Headers = $headers
    }

    if ($Body) {
        $params["Body"] = $Body
        $params["ContentType"] = "application/query+json"
    }

    return Invoke-RestMethod @params
}

$cosmosResourceLink = "dbs/$CosmosDatabaseName/colls/$CosmosContainerName"
$queryBody = @{
    query = "SELECT c.id, c.seq FROM c WHERE c.seq > @seedTicketMaxSeq"
    parameters = @(
        @{
            name = "@seedTicketMaxSeq"
            value = $SeedTicketMaxSeq
        }
    )
} | ConvertTo-Json -Depth 5

Write-Host "Querying Cosmos DB for non-seed tickets (seq > $SeedTicketMaxSeq)..." -ForegroundColor Cyan

$queryResult = Invoke-CosmosRest `
    -Verb "POST" `
    -ResourceType "docs" `
    -ResourceLink $cosmosResourceLink `
    -Path "$cosmosResourceLink/docs" `
    -Key $CosmosPrimaryKey `
    -Body $queryBody `
    -ExtraHeaders @{
        "x-ms-documentdb-isquery" = "True"
        "x-ms-documentdb-query-enablecrosspartition" = "True"
    }

$docsToDelete = @($queryResult.Documents)
if ($docsToDelete.Count -eq 0) {
    Write-Host "Cosmos cleanup: nothing to delete." -ForegroundColor Green
}
else {
    Write-Host "Deleting $($docsToDelete.Count) non-seed ticket document(s)..." -ForegroundColor Yellow
    foreach ($doc in $docsToDelete) {
        $docId = [string]$doc.id
        $docLink = "$cosmosResourceLink/docs/$docId"
        Invoke-CosmosRest `
            -Verb "DELETE" `
            -ResourceType "docs" `
            -ResourceLink $docLink `
            -Path "$docLink" `
            -Key $CosmosPrimaryKey `
            -ExtraHeaders @{
                "x-ms-documentdb-partitionkey" = "[`"$docId`"]"
            } | Out-Null
    }
    Write-Host "Cosmos cleanup complete." -ForegroundColor Green
}

Write-Host ""
Write-Host "Cleanup finished. Seed KB data and seed tickets were preserved." -ForegroundColor Cyan
