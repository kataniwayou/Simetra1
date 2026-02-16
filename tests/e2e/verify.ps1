#Requires -Version 5.1
<#
.SYNOPSIS
    End-to-end telemetry verification for Simetra.
    Proves heartbeat metric reaches Prometheus and enriched logs reach Elasticsearch.

.DESCRIPTION
    1. Starts OTel Collector, Prometheus, and Elasticsearch via docker-compose
    2. Starts Simetra in Development mode
    3. Polls Prometheus and Elasticsearch until telemetry arrives (with timeout)
    4. Tears everything down and reports results
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path "$ScriptDir\..\..")
$SimetraProject = Join-Path $RepoRoot 'src\Simetra'

# --- Helpers ---

function Write-Banner {
    param([string]$Message)
    Write-Host ''
    Write-Host '============================================' -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host '============================================' -ForegroundColor Cyan
}

function Write-Check {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Detail
    )
    if ($Passed) {
        Write-Host "[PASS] $Name`: $Detail" -ForegroundColor Green
    } else {
        Write-Host "[FAIL] $Name`: $Detail" -ForegroundColor Red
    }
}

function Invoke-WithRetry {
    param(
        [scriptblock]$Action,
        [int]$MaxAttempts = 12,
        [int]$DelaySeconds = 5,
        [string]$Description = 'operation'
    )
    for ($i = 1; $i -le $MaxAttempts; $i++) {
        try {
            return & $Action
        } catch {
            if ($i -eq $MaxAttempts) {
                throw "Timed out waiting for $Description after $($MaxAttempts * $DelaySeconds)s: $_"
            }
            Write-Host "  Waiting for $Description (attempt $i/$MaxAttempts)..." -ForegroundColor DarkGray
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

# POST JSON to Elasticsearch and return raw response body as string.
# Uses Invoke-WebRequest + raw Content to avoid PS 5.1 ConvertFrom-Json duplicate-key bug.
function Invoke-ES {
    param(
        [string]$Path,
        [string]$JsonBody = $null
    )
    $uri = "http://localhost:9200$Path"
    $params = @{
        Uri             = $uri
        UseBasicParsing = $true
        TimeoutSec      = 10
    }
    if ($JsonBody) {
        $params.Method      = 'Post'
        $params.Body        = [System.Text.Encoding]::UTF8.GetBytes($JsonBody)
        $params.ContentType = 'application/json; charset=utf-8'
    } else {
        $params.Method = 'Post'
    }
    $resp = Invoke-WebRequest @params
    return $resp.Content
}

# --- Results tracking ---

$results = [System.Collections.Generic.List[hashtable]]::new()

function Add-Result {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $results.Add(@{ Name = $Name; Passed = $Passed; Detail = $Detail })
    Write-Check -Name $Name -Passed $Passed -Detail $Detail
}

# --- Cleanup function ---

$simetraJob = $null

function Invoke-Cleanup {
    Write-Host ''
    Write-Host 'Tearing down...' -ForegroundColor Yellow

    if ($script:simetraJob) {
        try {
            Stop-Job -Job $script:simetraJob -ErrorAction SilentlyContinue
            Remove-Job -Job $script:simetraJob -Force -ErrorAction SilentlyContinue
        } catch { }
    }

    Push-Location $ScriptDir
    try {
        $prevPref = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        docker compose down -v 2>&1 | Out-Null
        $ErrorActionPreference = $prevPref
    } finally {
        Pop-Location
    }
}

# --- Main ---

try {
    Write-Banner 'Simetra E2E Telemetry Verification'

    # Step 1: Start infrastructure
    Write-Host ''
    Write-Host 'Step 1: Starting infrastructure (docker-compose up)...' -ForegroundColor Yellow
    Push-Location $ScriptDir
    try {
        $prevPref = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        docker compose up -d 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
        $ErrorActionPreference = $prevPref
        if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed' }
    } finally {
        Pop-Location
    }

    # Step 2: Wait for infrastructure readiness
    Write-Host ''
    Write-Host 'Step 2: Waiting for infrastructure readiness...' -ForegroundColor Yellow

    Invoke-WithRetry -Description 'Prometheus' -MaxAttempts 12 -DelaySeconds 5 -Action {
        $null = Invoke-WebRequest -Uri 'http://localhost:9090/-/ready' -Method Get -TimeoutSec 5 -UseBasicParsing
    }

    Invoke-WithRetry -Description 'Elasticsearch' -MaxAttempts 24 -DelaySeconds 5 -Action {
        $resp = Invoke-RestMethod -Uri 'http://localhost:9200/_cluster/health' -Method Get -TimeoutSec 5
        if ($resp.status -eq 'red') { throw 'Cluster health is red' }
    }

    Add-Result -Name 'Infrastructure ready' -Passed $true -Detail 'Prometheus + Elasticsearch responding'

    # Step 3: Start Simetra
    Write-Host ''
    Write-Host 'Step 3: Starting Simetra (dotnet run --environment Development)...' -ForegroundColor Yellow

    $simetraJob = Start-Job -ScriptBlock {
        param($ProjectPath)
        Set-Location $ProjectPath
        dotnet run --environment Development 2>&1
    } -ArgumentList $SimetraProject

    Start-Sleep -Seconds 5

    if ($simetraJob.State -eq 'Failed') {
        $jobOutput = Receive-Job -Job $simetraJob -ErrorAction SilentlyContinue
        throw "Simetra failed to start: $jobOutput"
    }

    Write-Host "  Simetra started (Job ID: $($simetraJob.Id), State: $($simetraJob.State))" -ForegroundColor DarkGray

    # Step 4: Poll for heartbeat metric (instead of fixed sleep)
    # Heartbeat fires every 15s, Prometheus scrapes every 5s. Poll until it appears.
    Write-Host ''
    Write-Host 'Step 4: Polling for heartbeat metric (timeout 120s)...' -ForegroundColor Yellow

    $heartbeatFound = $false
    $heartbeatDetail = ''
    $promQuery = [System.Uri]::EscapeDataString('beat{device_name="simetra-supervisor"}')

    for ($attempt = 1; $attempt -le 24; $attempt++) {
        Start-Sleep -Seconds 5
        try {
            $promResp = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=$promQuery" -Method Get -TimeoutSec 10

            if ($promResp.status -eq 'success' -and $promResp.data.result.Count -gt 0) {
                $firstResult = $promResp.data.result[0]
                $metricValue = $firstResult.value[1]
                $labels = $firstResult.metric
                $heartbeatDetail = "beat{site=$($labels.site), device_name=$($labels.device_name)} = $metricValue"
                $heartbeatFound = $true
                break
            }
        } catch { }
        Write-Host "  Polling for heartbeat metric (attempt $attempt/24)..." -ForegroundColor DarkGray
    }

    # Step 5: Prometheus checks
    Write-Host ''
    Write-Host 'Step 5: Running Prometheus checks...' -ForegroundColor Yellow

    # 5a: Heartbeat metric
    if ($heartbeatFound) {
        Add-Result -Name 'Heartbeat metric' -Passed $true -Detail $heartbeatDetail
    } else {
        Add-Result -Name 'Heartbeat metric' -Passed $false -Detail 'No results after 120s of polling'
    }

    # 5b: Runtime metrics
    try {
        $labelsResp = Invoke-RestMethod -Uri 'http://localhost:9090/api/v1/label/__name__/values' -Method Get -TimeoutSec 10

        if ($labelsResp.status -eq 'success') {
            $runtimeMetrics = $labelsResp.data | Where-Object {
                $_ -match 'process' -or $_ -match 'dotnet'
            }
            $count = ($runtimeMetrics | Measure-Object).Count

            if ($count -ge 3) {
                $sample = ($runtimeMetrics | Select-Object -First 3) -join ', '
                Add-Result -Name 'Runtime metrics' -Passed $true -Detail "$count series found ($sample, ...)"
            } else {
                Add-Result -Name 'Runtime metrics' -Passed $false -Detail "Only $count runtime metrics found (need >= 3)"
            }
        } else {
            Add-Result -Name 'Runtime metrics' -Passed $false -Detail "Label query failed: $($labelsResp.status)"
        }
    } catch {
        Add-Result -Name 'Runtime metrics' -Passed $false -Detail "Query failed: $_"
    }

    # Step 6: Elasticsearch checks
    Write-Host ''
    Write-Host 'Step 6: Running Elasticsearch checks...' -ForegroundColor Yellow

    # Poll for ES data availability (collector flushes logs asynchronously)
    $esDataReady = $false
    Write-Host '  Waiting for logs to appear in Elasticsearch...' -ForegroundColor DarkGray
    for ($esAttempt = 1; $esAttempt -le 12; $esAttempt++) {
        try {
            $null = Invoke-ES -Path '/simetra-logs/_refresh'
        } catch { }
        try {
            $countJson = Invoke-ES -Path '/simetra-logs/_count' -JsonBody '{"query":{"match_all":{}}}'
            $countMatch = [regex]::Match($countJson, '"count"\s*:\s*(\d+)')
            if ($countMatch.Success -and [int]$countMatch.Groups[1].Value -gt 0) {
                $esDataReady = $true
                Write-Host "  Elasticsearch has $($countMatch.Groups[1].Value) log records" -ForegroundColor DarkGray
                break
            }
        } catch { }
        Write-Host "  Waiting for Elasticsearch data (attempt $esAttempt/12)..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 5
    }

    if (-not $esDataReady) {
        Add-Result -Name 'Elasticsearch log enrichment' -Passed $false -Detail 'No log records appeared in simetra-logs after 60s'
        Add-Result -Name 'Heartbeat log in Elasticsearch' -Passed $false -Detail 'No data in index'
    } else {
        # Refresh index to ensure all buffered docs are searchable
        try { $null = Invoke-ES -Path '/simetra-logs/_refresh' } catch { }

        # 6a: Log enrichment fields
        try {
            $rawJson = Invoke-ES -Path '/simetra-logs/_search' `
                -JsonBody '{"query":{"match_all":{}},"size":1,"sort":[{"@timestamp":"desc"}]}'

            # Extract fields with regex (avoids PS 5.1 ConvertFrom-Json duplicate-key issue)
            $siteMatch = [regex]::Match($rawJson, '"Attributes\.site"\s*:\s*"([^"]*)"')
            $roleMatch = [regex]::Match($rawJson, '"Attributes\.role"\s*:\s*"([^"]*)"')
            $corrIdMatch = [regex]::Match($rawJson, '"Attributes\.correlationId"\s*:\s*"([^"]*)"')

            $siteValue = if ($siteMatch.Success) { $siteMatch.Groups[1].Value } else { $null }
            $roleValue = if ($roleMatch.Success) { $roleMatch.Groups[1].Value } else { $null }
            $corrIdValue = if ($corrIdMatch.Success) { $corrIdMatch.Groups[1].Value } else { $null }

            $hasSite = $siteValue -eq 'site-nyc-01'
            $hasRole = $roleValue -eq 'leader'
            $hasCorrId = -not [string]::IsNullOrWhiteSpace($corrIdValue)

            if ($hasSite -and $hasRole -and $hasCorrId) {
                Add-Result -Name 'Elasticsearch log enrichment' -Passed $true -Detail "site=$siteValue, role=$roleValue, correlationId present"
            } else {
                $detail = "site=$siteValue (expect site-nyc-01), role=$roleValue (expect leader), correlationId=$(if($hasCorrId){'present'}else{'MISSING'})"
                Add-Result -Name 'Elasticsearch log enrichment' -Passed $false -Detail $detail
            }
        } catch {
            Add-Result -Name 'Elasticsearch log enrichment' -Passed $false -Detail "Query failed: $_"
        }

        # 6b: Heartbeat log
        try {
            $hbJson = Invoke-ES -Path '/simetra-logs/_search' `
                -JsonBody '{"query":{"match_phrase":{"Body":"Heartbeat trap sent"}},"size":1}'

            $hbTotalMatch = [regex]::Match($hbJson, '"total"\s*:\s*\{\s*"value"\s*:\s*(\d+)')
            $hbHits = if ($hbTotalMatch.Success) { [int]$hbTotalMatch.Groups[1].Value } else { 0 }

            if ($hbHits -ge 1) {
                Add-Result -Name 'Heartbeat log in Elasticsearch' -Passed $true -Detail "`"Heartbeat trap sent`" found ($hbHits hits)"
            } else {
                Add-Result -Name 'Heartbeat log in Elasticsearch' -Passed $false -Detail 'No heartbeat log records found'
            }
        } catch {
            Add-Result -Name 'Heartbeat log in Elasticsearch' -Passed $false -Detail "Query failed: $_"
        }
    }

    # Step 7: Print results summary
    $passCount = ($results | Where-Object { $_.Passed }).Count
    $totalCount = $results.Count

    Write-Banner "Result: $passCount/$totalCount PASSED"

    # Step 8-9: Cleanup (in finally block)

    # Step 10: Exit code
    if ($passCount -eq $totalCount) {
        exit 0
    } else {
        exit 1
    }

} catch {
    Write-Host ''
    Write-Host "FATAL: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed
    exit 1
} finally {
    Invoke-Cleanup
}
