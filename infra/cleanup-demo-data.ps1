<#
.SYNOPSIS
    Removes demo-created data from AI Search, Cosmos DB, Redis, and Blob Storage
    while preserving repository seed data.

.DESCRIPTION
    This script is intended for regression-cycle cleanup. It preserves:
      - KB articles whose IDs are present in infra/seed-data.json
      - Seeded ticket documents whose seq is <= SeedTicketMaxSeq
      - Long-term Redis memory (ltm:*) unless -ClearLongTermMemory is supplied
      - Recent eval blobs (within BlobAgeDays) and all non-eval blobs

    It deletes:
      - Agent-indexed or manually added KB documents not present in seed-data.json
      - Cosmos ticket documents above the configured seed threshold
      - Ephemeral Redis state for chat history, attachments, usage snapshots, and retry-safe side-effect ledgers
      - (-CleanBlobs) Eval result blobs in the eval-results container older than BlobAgeDays days

    Switches -CleanBlobs and -ClearLongTermMemory are opt-in and off by default.
#>

param(
    [Parameter(Mandatory)][string]$SearchEndpoint,
    [Parameter(Mandatory)][string]$SearchAdminKey,
    [Parameter(Mandatory)][string]$CosmosEndpoint,
    [Parameter(Mandatory)][string]$CosmosPrimaryKey,
    [string]$SearchIndexName = "helpdesk-kb",
    [string]$CosmosDatabaseName = "helpdeskdb",
    [string]$CosmosContainerName = "tickets",
    [int]$SeedTicketMaxSeq = 1013,
    [string]$RedisContainerAppName,
    [string]$RedisResourceGroupName,
    [switch]$ClearLongTermMemory,
    # Blob cleanup — requires -BlobConnectionString (or $env:AZURE_STORAGE_CONNECTION_STRING).
    [switch]$CleanBlobs,
    [string]$BlobConnectionString,
    [string]$EvalBlobContainer = "eval-results",
    [int]$BlobAgeDays = 30
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
if ($RedisContainerAppName -and $RedisResourceGroupName) {
    Write-Host "Cleaning Redis ephemeral state through Container App '$RedisContainerAppName'..." -ForegroundColor Cyan

    $patterns = @(
        "messages:*",
        "usage:*",
        "attachments:*",
        "sideeffect:*"
    )

    if ($ClearLongTermMemory) {
        # Delete ALL long-term memory profiles (helpdesk:ltm:* pattern via the key prefix).
        $patterns += "ltm:*"
        Write-Host "  Long-term memory (ltm:*) will also be cleared." -ForegroundColor Yellow
    }

    foreach ($pattern in $patterns) {
        Write-Host "Deleting Redis keys matching '$pattern'..." -ForegroundColor Yellow
        $redisCommand = "sh -lc ""redis-cli --scan --pattern '$pattern' | xargs -r redis-cli del >/dev/null"""
        az containerapp exec `
            --name $RedisContainerAppName `
            --resource-group $RedisResourceGroupName `
            --command $redisCommand `
            --only-show-errors | Out-Null
    }

    Write-Host "Redis cleanup complete." -ForegroundColor Green
}
else {
    Write-Host "Redis cleanup skipped. Supply -RedisContainerAppName and -RedisResourceGroupName to clear ephemeral Redis state." -ForegroundColor DarkYellow
}

Write-Host ""
if ($CleanBlobs) {
    $connStr = if ($BlobConnectionString) { $BlobConnectionString } else { $env:AZURE_STORAGE_CONNECTION_STRING }
    if (-not $connStr) {
        Write-Warning "Blob cleanup skipped: provide -BlobConnectionString or set `$env:AZURE_STORAGE_CONNECTION_STRING."
    }
    else {
        $cutoff = (Get-Date).AddDays(-$BlobAgeDays).ToString("o")
        Write-Host "Cleaning eval blobs in '$EvalBlobContainer' older than $BlobAgeDays days (before $cutoff)..." -ForegroundColor Cyan

        # List blobs and filter by LastModified < cutoff to avoid the --if-unmodified-since
        # batch quirk that deletes EVERYTHING when the header is mis-formatted.
        $blobList = az storage blob list `
            --container-name $EvalBlobContainer `
            --connection-string $connStr `
            --query "[].{name:name, modified:properties.lastModified}" `
            --output json `
            --only-show-errors 2>$null | ConvertFrom-Json

        if ($null -eq $blobList -or $blobList.Count -eq 0) {
            Write-Host "Blob cleanup: no blobs found in '$EvalBlobContainer'." -ForegroundColor Green
        }
        else {
            $toDelete = $blobList | Where-Object { [datetime]$_.modified -lt [datetime]$cutoff }
            if ($toDelete.Count -eq 0) {
                Write-Host "Blob cleanup: no blobs older than $BlobAgeDays days. Nothing to delete." -ForegroundColor Green
            }
            else {
                Write-Host "Found $($toDelete.Count) blob(s) to delete. Proceeding..." -ForegroundColor Yellow
                foreach ($blob in $toDelete) {
                    az storage blob delete `
                        --container-name $EvalBlobContainer `
                        --name $blob.name `
                        --connection-string $connStr `
                        --only-show-errors | Out-Null
                    Write-Host "  Deleted: $($blob.name)" -ForegroundColor DarkGray
                }
                Write-Host "Blob cleanup complete. $($toDelete.Count) eval blob(s) removed." -ForegroundColor Green
            }
        }
    }
}
else {
    Write-Host "Blob cleanup skipped. Pass -CleanBlobs (with -BlobConnectionString or `$env:AZURE_STORAGE_CONNECTION_STRING) to remove old eval results." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "Cleanup finished. Seed KB data and seed tickets were preserved." -ForegroundColor Cyan
if (-not $ClearLongTermMemory) {
    Write-Host "Long-term memory in Redis was preserved." -ForegroundColor Cyan
}
