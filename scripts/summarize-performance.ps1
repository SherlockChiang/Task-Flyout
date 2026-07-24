param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path
)

$rows = foreach ($item in $Path) {
    if (-not (Test-Path -LiteralPath $item -PathType Leaf)) {
        throw "Performance diagnostics file not found: $item"
    }
    Import-Csv -LiteralPath $item
}

function Get-NearestRank {
    param([double[]]$Values, [int]$Percentile)
    if ($Values.Count -eq 0) { return $null }
    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100.0) * $ordered.Count) - 1
    return $ordered[$index]
}

$runCount = @($rows.run_id | Where-Object { $_ } | Sort-Object -Unique).Count
$rows |
    Group-Object scenario, metric, source |
    ForEach-Object {
        $group = @($_.Group)
        $success = @($group | Where-Object outcome -eq 'success')
        $durations = [double[]]@($success | ForEach-Object { [double]::Parse($_.duration, [Globalization.CultureInfo]::InvariantCulture) })
        $observedRuns = @($group.run_id | Sort-Object -Unique).Count
        [pscustomobject]@{
            scenario = $group[0].scenario
            metric = $group[0].metric
            source = $group[0].source
            samples = $durations.Count
            p50_ms = if ($durations.Count) { [Math]::Round((Get-NearestRank $durations 50), 3) } else { $null }
            p95_ms = if ($durations.Count -ge 20) { [Math]::Round((Get-NearestRank $durations 95), 3) } else { $null }
            success = $success.Count
            failure = @($group | Where-Object outcome -eq 'failure').Count
            missing = [Math]::Max(0, $runCount - $observedRuns)
        }
    } |
    Sort-Object scenario, metric, source |
    Format-Table -AutoSize
