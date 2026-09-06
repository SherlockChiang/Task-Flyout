param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path,
    # Opt in only after checking that the runs used the same fixture/power settings.
    [switch]$CombineRuns,
    [switch]$AsObject
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$seen = @{}
$rows = @(foreach ($item in $Path) {
    if (-not (Test-Path -LiteralPath $item -PathType Leaf)) {
        throw "Performance diagnostics file not found: $item"
    }
    foreach ($row in (Import-Csv -LiteralPath $item)) {
        foreach ($field in @('run_id', 'sequence', 'scenario', 'metric', 'source', 'outcome', 'duration')) {
            if ([string]::IsNullOrWhiteSpace($row.$field)) { throw "Missing $field in $item" }
        }
        $version = if ($row.schema_version) { $row.schema_version } else { '1' }
        if ($version -notin @('1', '2')) { throw "Unsupported performance schema: $version" }
        if ($version -eq '2') {
            foreach ($field in @('build_id', 'build_version', 'configuration', 'architecture', 'sample_kind', 'timing_basis', 'dropped_before', 'write_failures_before')) {
                if ([string]::IsNullOrWhiteSpace($row.$field)) { throw "Missing v2 field $field in $item" }
            }
            if ($row.sample_kind -in @('cold_attempt', 'warm_attempt') -and [string]::IsNullOrWhiteSpace($row.operation_id)) {
                throw "Missing interaction operation_id in $item"
            }
            foreach ($field in @('dropped_before', 'write_failures_before')) {
                if ([long]::Parse($row.$field, $culture) -lt 0) { throw "Invalid $field in $item" }
            }
        }
        $duration = [double]::Parse($row.duration, $culture)
        if ([double]::IsNaN($duration) -or [double]::IsInfinity($duration) -or $duration -lt 0) {
            throw "Invalid duration in $item"
        }
        $sequence = [long]::Parse($row.sequence, $culture)
        if ($sequence -lt 1) { throw "Invalid sequence in $item" }
        $key = "$($row.run_id):$sequence"
        $serialized = $row | ConvertTo-Json -Compress
        if ($seen.ContainsKey($key)) {
            if ($seen[$key] -ne $serialized) { throw "Conflicting duplicate performance record: $key" }
            continue
        }
        $seen[$key] = $serialized
        $row | Add-Member -NotePropertyMembers @{
            schema = $version
            build = if ($version -eq '2') { $row.build_id } else { 'legacy_unknown' }
            config = if ($version -eq '2') { $row.configuration } else { 'unknown' }
            arch = if ($version -eq '2') { $row.architecture } else { 'unknown' }
            kind = if ($version -eq '2') { $row.sample_kind } else { 'legacy' }
            basis = if ($version -eq '2') { $row.timing_basis } else { 'wall_clock' }
            run_group = if ($CombineRuns -and $version -eq '2') { 'combined' } else { $row.run_id }
            duration_ms = $duration
        }
        $row
    }
})

function Get-NearestRank {
    param([double[]]$Values, [int]$Percentile)
    if ($Values.Count -eq 0) { return $null }
    $ordered = @($Values | Sort-Object)
    $index = [Math]::Ceiling(($Percentile / 100.0) * $ordered.Count) - 1
    return $ordered[$index]
}

# A loss anywhere in a run can bias any of that run's successful distributions.
$incompleteRuns = @{}
foreach ($run in ($rows | Group-Object run_id)) {
    $sequences = @($run.Group | ForEach-Object { [long]$_.sequence } | Sort-Object)
    $hasGap = $sequences.Count -gt 0 -and ($sequences[0] -ne 1 -or $sequences[-1] -ne $sequences.Count)
    $loss = @($run.Group | Where-Object { [long]$_.dropped_before -gt 0 -or [long]$_.write_failures_before -gt 0 }).Count -gt 0
    $incompleteRuns[$run.Name] = $hasGap -or $loss
}

$summary = @($rows |
    Group-Object schema, build, build_version, config, arch, run_group, scenario, metric, source, kind, basis |
    ForEach-Object {
        $group = @($_.Group)
        $first = $group[0]
        $success = @($group | Where-Object outcome -eq 'success')
        $durations = [double[]]@($success | ForEach-Object { $_.duration_ms })
        $terminalIds = @{}
        foreach ($row in $group) {
            if ($row.operation_id -and $row.outcome -ne 'started') { $terminalIds[$row.operation_id] = $true }
        }
        $unfinished = @($group | Where-Object { $_.outcome -eq 'started' -and -not $terminalIds.ContainsKey($_.operation_id) }).Count
        $incomplete = $unfinished -gt 0 -or @($group | Where-Object { $incompleteRuns[$_.run_id] }).Count -gt 0
        $isDuration = $first.kind -notin @('milestone', 'status')
        [pscustomobject]@{
            schema = $first.schema
            build_id = $first.build
            build_version = $first.build_version
            configuration = $first.config
            architecture = $first.arch
            run_group = $first.run_group
            scenario = $first.scenario
            metric = $first.metric
            source = $first.source
            sample_kind = $first.kind
            timing_basis = $first.basis
            runs = @($group.run_id | Sort-Object -Unique).Count
            samples = $durations.Count
            p50_ms = if ($isDuration -and $durations.Count -and -not $incomplete) { [Math]::Round((Get-NearestRank $durations 50), 3) } else { $null }
            p95_ms = if ($isDuration -and $durations.Count -ge 20 -and -not $incomplete) { [Math]::Round((Get-NearestRank $durations 95), 3) } else { $null }
            failure = @($group | Where-Object outcome -eq 'failure').Count
            cancelled = @($group | Where-Object outcome -eq 'cancelled').Count
            superseded = @($group | Where-Object outcome -eq 'superseded').Count
            ignored = @($group | Where-Object { $_.outcome -in @('ignored', 'not_opening') }).Count
            other = @($group | Where-Object { $_.outcome -notin @('started', 'success', 'failure', 'cancelled', 'superseded', 'ignored', 'not_opening') }).Count
            unfinished = $unfinished
            incomplete = $incomplete
        }
    } | Sort-Object build_id, configuration, run_group, scenario, metric, sample_kind)

if ($AsObject) { $summary }
else {
    $summary | Format-Table schema, configuration, build_id, build_version, architecture, run_group, scenario, metric, source, sample_kind, timing_basis, samples, p50_ms, p95_ms, failure, cancelled, superseded, ignored, other, unfinished, incomplete -AutoSize
}
