param()

$ErrorActionPreference = 'Stop'
$summaryScript = Join-Path $PSScriptRoot 'summarize-performance.ps1'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('taskflyout-summary-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$files = [Collections.Generic.List[string]]::new()
$checks = 0

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Save-Fixture([string]$Name, [object[]]$Rows) {
    $path = Join-Path $directory $Name
    $Rows | Export-Csv -LiteralPath $path -NoTypeInformation -Encoding UTF8
    $files.Add($path)
    return $path
}

function New-Sample([int]$Sequence, [string]$Run = 'run-a', [double]$Duration = $Sequence) {
    [pscustomobject][ordered]@{
        run_id = $Run; sequence = $Sequence; timestamp = '2026-09-07T00:00:00Z'
        scenario = 'flyout_open'; metric = 'command_to_rendering'
        start = '2026-09-07T00:00:00Z'; end = '2026-09-07T00:00:00Z'
        duration = $Duration.ToString([Globalization.CultureInfo]::InvariantCulture)
        outcome = 'success'; source = 'tray'; schema_version = '2'; build_id = 'build-a'
        build_version = 'test+source-a'; configuration = 'Release'; architecture = 'X64'
        operation_id = "$Run-operation-$Sequence"; sample_kind = 'warm_attempt'; timing_basis = 'monotonic'
        dropped_before = '0'; write_failures_before = '0'
    }
}

try {
    $warm = @(1..20 | ForEach-Object { New-Sample $_ })
    $path = Save-Fixture 'warm.csv' $warm
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result.Count -eq 1 -and $result[0].samples -eq 20 -and $result[0].p50_ms -eq 10 -and $result[0].p95_ms -eq 19) 'Nearest-rank percentile mismatch.'
    $result = @(& $summaryScript -Path @($path, $path) -AsObject)
    Assert-Check ($result.Count -eq 1 -and $result[0].samples -eq 20) 'Duplicate input inflated sample count.'

    $splitRuns = @(1..10 | ForEach-Object { New-Sample $_ }) + @(1..10 | ForEach-Object { New-Sample $_ 'run-b' ($_ + 10) })
    $path = Save-Fixture 'runs.csv' $splitRuns
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result.Count -eq 2 -and @($result | Where-Object { $null -ne $_.p95_ms }).Count -eq 0) 'Runs were implicitly pooled.'
    $result = @(& $summaryScript -Path $path -CombineRuns -AsObject)
    Assert-Check ($result.Count -eq 1 -and $result[0].samples -eq 20 -and $result[0].p95_ms -eq 19) 'Explicit same-build run combination failed.'

    foreach ($field in @('build_id', 'configuration', 'sample_kind', 'timing_basis', 'architecture')) {
        $other = New-Sample 1 'run-other'
        $other.$field = 'different'
        $path = Save-Fixture "$field.csv" ($warm + @($other))
        $result = @(& $summaryScript -Path $path -CombineRuns -AsObject)
        Assert-Check ($result.Count -eq 2) "Mixed $field values were pooled."
    }

    $cancelled = New-Sample 21
    $cancelled.outcome = 'cancelled'
    $superseded = New-Sample 22
    $superseded.outcome = 'superseded'
    $path = Save-Fixture 'outcomes.csv' ($warm + @($cancelled, $superseded))
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result[0].samples -eq 20 -and $result[0].cancelled -eq 1 -and $result[0].superseded -eq 1 -and $result[0].p95_ms -eq 19) 'Non-success outcomes polluted percentiles or were lost.'

    $started = New-Sample 21
    $started.outcome = 'started'
    $path = Save-Fixture 'unfinished.csv' ($warm + @($started))
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result[0].unfinished -eq 1 -and $result[0].incomplete -and $null -eq $result[0].p95_ms) 'Unfinished operation must suppress percentiles.'
    $finished = New-Sample 22
    $finished.operation_id = $started.operation_id
    $path = Save-Fixture 'finished.csv' ($warm + @($started, $finished))
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result[0].unfinished -eq 0 -and -not $result[0].incomplete -and $result[0].samples -eq 21) 'Started record was counted as a duration or failed to match completion.'

    foreach ($field in @('dropped_before', 'write_failures_before')) {
        $loss = New-Sample 21
        $loss.$field = '1'
        $path = Save-Fixture "$field.csv" ($warm + @($loss))
        $result = @(& $summaryScript -Path $path -AsObject)
        Assert-Check ($result[0].incomplete -and $null -eq $result[0].p95_ms) "Loss in $field must suppress percentiles."
    }
    $path = Save-Fixture 'gap.csv' ($warm + @(New-Sample 22))
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result[0].incomplete -and $null -eq $result[0].p95_ms) 'Sequence gap was hidden.'

    $path = Save-Fixture 'prefix.csv' @($warm | Select-Object -Skip 1)
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($result[0].incomplete -and $null -eq $result[0].p50_ms) 'Missing rotated prefix was hidden.'

    $status = New-Sample 1 'status-run'
    $status.sample_kind = 'status'
    $path = Save-Fixture 'status.csv' @($status)
    $result = @(& $summaryScript -Path $path -AsObject)
    Assert-Check ($null -eq $result[0].p50_ms) 'Status record was reported as a duration.'

    $legacy = @($splitRuns | Select-Object run_id, sequence, timestamp, scenario, metric, start, end, duration, outcome, source)
    $path = Save-Fixture 'legacy.csv' $legacy
    $result = @(& $summaryScript -Path $path -CombineRuns -AsObject)
    Assert-Check ($result.Count -eq 2 -and @($result | Where-Object schema -ne '1').Count -eq 0) 'Legacy records must stay isolated by run even with CombineRuns.'

    $invalid = New-Sample 1
    $invalid.duration = 'NaN'
    $path = Save-Fixture 'invalid.csv' @($invalid)
    $rejected = $false
    try { & $summaryScript -Path $path -AsObject | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected 'Nonfinite duration was accepted.'

    $conflict = New-Sample 1
    $conflict.duration = '999'
    $path = Save-Fixture 'conflict.csv' ($warm + @($conflict))
    $rejected = $false
    try { & $summaryScript -Path $path -AsObject | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected 'Conflicting duplicate record was silently discarded.'
    Write-Output "Performance summary checks passed: $checks (synthetic fixtures, not runtime measurements)."
}
finally {
    foreach ($path in $files) { Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $directory -ErrorAction SilentlyContinue
}
