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

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TaskFlyoutSoakNativeMethods
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maximumCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr FindMainWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint ownerProcessId);
            if (ownerProcessId != processId || !IsWindowVisible(hwnd)) return true;

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() != "Task Flyout") return true;

            result = hwnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static bool CloseWindow(IntPtr hwnd) => PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
}
"@

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

# The soak measures the documented tray-idle state. A shell activation can expose the
# main window, especially after the preceding packaged smoke run, so close it to the tray.
$windowDeadline = (Get-Date).AddSeconds(5)
do {
    $mainWindow = [TaskFlyoutSoakNativeMethods]::FindMainWindow($process.Id)
    if ($mainWindow -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $windowDeadline)
if ($mainWindow -ne [IntPtr]::Zero) {
    if (-not [TaskFlyoutSoakNativeMethods]::CloseWindow($mainWindow)) {
        throw "The Task Flyout main window could not be closed to the tray."
    }
    $closeDeadline = (Get-Date).AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 250
        $mainWindow = [TaskFlyoutSoakNativeMethods]::FindMainWindow($process.Id)
    } while ($mainWindow -ne [IntPtr]::Zero -and (Get-Date) -lt $closeDeadline)
    if ($mainWindow -ne [IntPtr]::Zero) {
        throw "The Task Flyout main window did not close to the tray."
    }
}

# Exclude normal CLR, WinUI, tray-icon, and background-service initialization from the baseline.
Start-Sleep -Seconds 60
$process.Refresh()
if ($process.HasExited) { throw "Task Flyout exited during tray-idle warm-up." }

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
