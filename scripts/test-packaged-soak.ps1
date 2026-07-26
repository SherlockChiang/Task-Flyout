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
}

[ComImport]
[Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
public class ApplicationActivationManager
{
}

[ComImport]
[Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IApplicationActivationManager
{
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);
}

public static class TaskFlyoutSoakApplicationActivator
{
    public static int Activate(string appUserModelId)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        int result = manager.ActivateApplication(
            appUserModelId,
            "----AppNotificationActivated:action=openAgenda",
            0,
            out uint processId);
        Marshal.ThrowExceptionForHR(result);
        return unchecked((int)processId);
    }
}
"@

$package = Get-AppxPackage -Name $PackageName | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $package -or $package.Status -ne "Ok") { throw "A healthy $PackageName package is not installed." }

$outputParent = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputParent)) {
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
}

$processId = [TaskFlyoutSoakApplicationActivator]::Activate("$($package.PackageFamilyName)!App")
$deadline = (Get-Date).AddSeconds(30)
do {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $process) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $deadline)
if ($null -eq $process) { throw "Task Flyout did not start." }

# The notification activation above intentionally has no valid target and must leave the
# app in its documented tray-idle state without ever constructing the main window.
$windowDeadline = (Get-Date).AddSeconds(5)
do {
    $mainWindow = [TaskFlyoutSoakNativeMethods]::FindMainWindow($process.Id)
    if ($mainWindow -ne [IntPtr]::Zero) { break }
    Start-Sleep -Milliseconds 250
} while ((Get-Date) -lt $windowDeadline)
if ($mainWindow -ne [IntPtr]::Zero) {
    throw "Task Flyout unexpectedly created a main window during tray-idle activation."
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
