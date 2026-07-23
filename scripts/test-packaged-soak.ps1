param(
    [string]$PackageName = "Uranus92.TaskFlyout",
    [int]$DurationMinutes = $(if ($env:TASKFLYOUT_SOAK_MINUTES) { [int]$env:TASKFLYOUT_SOAK_MINUTES } else { 10 }),
    [int]$SampleSeconds = 10,
    [int]$MaxHandleGrowth = $(if ($env:TASKFLYOUT_SOAK_MAX_HANDLE_GROWTH) { [int]$env:TASKFLYOUT_SOAK_MAX_HANDLE_GROWTH } else { 25 }),
    [int]$MaxPrivateMemoryGrowthMb = $(if ($env:TASKFLYOUT_SOAK_MAX_PRIVATE_MB_GROWTH) { [int]$env:TASKFLYOUT_SOAK_MAX_PRIVATE_MB_GROWTH } else { 64 }),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\TestResults\packaged-soak.csv")
)

$ErrorActionPreference = "Stop"
if ($DurationMinutes -lt 1) { throw "DurationMinutes must be at least 1." }
if ($SampleSeconds -lt 1) { throw "SampleSeconds must be at least 1." }

$package = Get-AppxPackage -Name $PackageName | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $package -or $package.Status -ne "Ok") { throw "A healthy $PackageName package is not installed." }

$outputParent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputParent)) {
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}

Start-Process explorer.exe "shell:AppsFolder\$($package.PackageFamilyName)!App"
$deadline = (Get-Date).AddSeconds(30)
do {
    $process = Get-Process -Name "Task_Flyout" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $process) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)
if ($null -eq $process) { throw "Task Flyout did not start." }

$samples = [System.Collections.Generic.List[object]]::new()
$end = (Get-Date).AddMinutes($DurationMinutes)
try {
    while ((Get-Date) -lt $end) {
        $process.Refresh()
        if ($process.HasExited) { throw "Task Flyout exited during the soak run." }
        $samples.Add([pscustomobject]@{
            TimestampUtc = [DateTimeOffset]::UtcNow.ToString("O")
            ProcessId = $process.Id
            Handles = $process.HandleCount
            WorkingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 2)
            PrivateMemoryMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            Threads = $process.Threads.Count
        })
        Start-Sleep -Seconds $SampleSeconds
    }
}
finally {
    $samples | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8
    Get-Process -Name "Task_Flyout" -ErrorAction SilentlyContinue | Stop-Process -Force
}

if ($samples.Count -lt 6) { throw "The soak run did not produce enough samples." }
function Get-Median([double[]]$Values) {
    $sorted = $Values | Sort-Object
    return $sorted[[math]::Floor($sorted.Count / 2)]
}

$first = $samples | Select-Object -First 3
$last = $samples | Select-Object -Last 3
$handleGrowth = (Get-Median @($last.Handles)) - (Get-Median @($first.Handles))
$privateGrowth = (Get-Median @($last.PrivateMemoryMb)) - (Get-Median @($first.PrivateMemoryMb))
if ($handleGrowth -gt $MaxHandleGrowth) {
    throw "Handle growth $handleGrowth exceeded the limit $MaxHandleGrowth. See $OutputPath."
}
if ($privateGrowth -gt $MaxPrivateMemoryGrowthMb) {
    throw "Private-memory growth $privateGrowth MB exceeded the limit $MaxPrivateMemoryGrowthMb MB. See $OutputPath."
}

Write-Host "Packaged soak passed. Handle growth: $handleGrowth; private-memory growth: $privateGrowth MB." -ForegroundColor Green
