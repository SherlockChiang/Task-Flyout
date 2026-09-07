<#
.SYNOPSIS
Starts the accepted installed Release build with process-local performance logging.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion = '1.4.4.6',
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$ExpectedAssemblySha256 = '3F97268AFB32168C7DE75483A9EE95508FB544D0BCDAC8119AEEE564E8B0D7BD',
    [ValidatePattern('^[a-fA-F0-9]{32}$')]
    [string]$ExpectedBuildId = 'baa8f3b20cd14ae2ab0518a1050732a3',
    [ValidateRange(1, 60)]
    [int]$LogTimeoutSeconds = 15,
    [switch]$DescribeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$running = @(Get-Process -Name Task_Flyout -ErrorAction SilentlyContinue)
if (-not $DescribeOnly -and $running.Count) {
    throw 'Task Flyout is still running. Exit from its tray menu first; no process was stopped or started.'
}
$packages = @(Get-AppxPackage -Name Uranus92.TaskFlyout)
if ($packages.Count -ne 1 -or $packages[0].Status -ne 'Ok' -or
    $packages[0].Publisher -ne 'CN=FDE9F71C-A397-410B-81EE-36E18F4325E5' -or
    [string]$packages[0].Version -ne $ExpectedVersion) {
    throw 'The expected healthy installed package is not registered. No launch attempted.'
}
$package = $packages[0]
$exe = Join-Path $package.InstallLocation 'Task_Flyout.exe'
$assembly = Join-Path $package.InstallLocation 'Task_Flyout.dll'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
    (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $ExpectedAssemblySha256) {
    throw 'Installed payload does not match the selected control. No launch attempted.'
}
$local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$logPaths = @(
    (Join-Path $local 'TaskFlyout\Logs\performance-diagnostics-v2.csv'),
    (Join-Path $local "Packages\$($package.PackageFamilyName)\LocalCache\Local\TaskFlyout\Logs\performance-diagnostics-v2.csv")
)
if ($DescribeOnly) {
    [pscustomobject]@{
        Version = $ExpectedVersion; BuildId = $ExpectedBuildId; Executable = $exe
        CanStart = $running.Count -eq 0; RunningProcessIds = @($running | ForEach-Object Id)
        PerformanceLogging = $true; StandaloneTaskbarSuppressed = $true
        ChangesPersistentSettings = $false; ForceStopsProcesses = $false; LogPaths = $logPaths
    }
    return
}

function Read-PerformanceRows([string]$Path, [switch]$AllowSharingRetry) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        try { Import-Csv -LiteralPath $Path -ErrorAction Stop }
        catch [IO.IOException] { if (-not $AllowSharingRetry) { throw } }
    }
}
$previousRuns = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $logPaths) {
    foreach ($row in (@(Read-PerformanceRows $path) + @(Read-PerformanceRows ($path + '.1')))) {
        if ($row.PSObject.Properties['run_id']) { [void]$previousRuns.Add([string]$row.run_id) }
    }
}
# Recheck after preflight to avoid knowingly starting a second instance.
if (@(Get-Process -Name Task_Flyout -ErrorAction SilentlyContinue).Count) {
    throw 'Task Flyout started during preflight. No launch attempted.'
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $exe
$start.WorkingDirectory = $package.InstallLocation
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$start.EnvironmentVariables['TASKFLYOUT_PERFORMANCE_DIAGNOSTICS'] = '1'
# Keep the archived native-injection experiment disabled, for this process only.
$start.EnvironmentVariables['TASKFLYOUT_DISABLE_STANDALONE_TASKBAR'] = '1'
$launchedAt = [DateTimeOffset]::UtcNow
$process = [Diagnostics.Process]::Start($start)
try {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($LogTimeoutSeconds)
    do {
        if ($process.HasExited) { throw "The diagnostic app exited (code $($process.ExitCode))." }
        foreach ($path in $logPaths) {
            foreach ($row in @(Read-PerformanceRows $path -AllowSharingRetry)) {
                $timestamp = [DateTimeOffset]::MinValue
                if ($row.PSObject.Properties['build_id'] -and $row.PSObject.Properties['run_id'] -and
                    $row.PSObject.Properties['configuration'] -and $row.PSObject.Properties['schema_version'] -and
                    $row.PSObject.Properties['timestamp'] -and [DateTimeOffset]::TryParse($row.timestamp, [ref]$timestamp) -and
                    $timestamp -ge $launchedAt -and
                    $row.schema_version -eq '2' -and $row.configuration -eq 'Release' -and
                    $row.build_id -eq $ExpectedBuildId -and -not [string]::IsNullOrWhiteSpace($row.run_id) -and
                    -not $previousRuns.Contains($row.run_id)) {
                    [pscustomobject]@{
                        Status = 'StartedAndLogging'; ProcessId = $process.Id; Version = $ExpectedVersion
                        RunId = $row.run_id; BuildId = $row.build_id; LogPath = $path
                        StandaloneTaskbarSuppressed = $true
                    }
                    return
                }
            }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Process $($process.Id) started but no matching fresh Release log was found. Do not count samples yet; the process was left running."
}
finally { $process.Dispose() }
