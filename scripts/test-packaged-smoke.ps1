param(
    [string]$PackageName = "Uranus92.TaskFlyout",
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class TaskFlyoutSmokeNativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int length);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    public static IntPtr FindMainWindow(uint targetProcessId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, parameter) =>
        {
            uint processId;
            GetWindowThreadProcessId(hwnd, out processId);
            if (processId != targetProcessId) return true;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() != "Task Flyout") return true;
            result = hwnd;
            return false;
        }, IntPtr.Zero);
        return result;
    }
}
"@
Add-Type @"
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(string appUserModelId, string arguments, uint options, out uint processId);
    void ActivateForFile();
    void ActivateForProtocol();
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager { }

public static class TaskFlyoutApplicationActivator
{
    public static uint Activate(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        uint processId;
        Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, arguments, 0, out processId));
        return processId;
    }
}
"@

function Get-TestPackage {
    $package = Get-AppxPackage -Name $PackageName |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($null -eq $package -or $package.Status -ne "Ok") {
        throw "A healthy $PackageName package is not installed."
    }
    return $package
}

function Start-TestApplication {
    param([string]$PackageFamilyName)

    $processId = [TaskFlyoutApplicationActivator]::Activate(
        "$PackageFamilyName!App",
        "----AppNotificationActivated:action=smoke")
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            $process.Refresh()
            if ([TaskFlyoutSmokeNativeMethods]::FindMainWindow($process.Id) -ne [IntPtr]::Zero) { return $process }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    throw "Task Flyout did not expose a main window within $TimeoutSeconds seconds."
}

function Get-RootElement {
    param([System.Diagnostics.Process]$Process)
    $handle = [TaskFlyoutSmokeNativeMethods]::FindMainWindow($Process.Id)
    if ($handle -eq [IntPtr]::Zero) { throw "The Task Flyout main window is not visible." }
    return [System.Windows.Automation.AutomationElement]::FromHandle($handle)
}

function Find-AutomationElement {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [switch]$Optional
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($Optional) { return $null }
    throw "Automation element '$AutomationId' was not found."
}

function Invoke-AutomationElement {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
        return
    }
    $Element.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
}

function Assert-NamedElement {
    param([System.Windows.Automation.AutomationElement]$Element, [string]$AutomationId)
    if ([string]::IsNullOrWhiteSpace($Element.Current.Name)) {
        throw "Automation element '$AutomationId' has no accessible name."
    }
}

$package = Get-TestPackage
$process = Start-TestApplication -PackageFamilyName $package.PackageFamilyName
try {
    $root = Get-RootElement -Process $process
    Find-AutomationElement -Root $root -AutomationId "MainNavigation" | Out-Null

    foreach ($target in @(
        @{ Navigation = "NavTasks"; Page = "TasksToggleAccounts" },
        @{ Navigation = "NavMail"; Page = "MailComposeButton" },
        @{ Navigation = "NavCalendar"; Page = "CalendarToggleAccounts" }
    )) {
        $navigation = Find-AutomationElement -Root $root -AutomationId $target.Navigation
        Assert-NamedElement -Element $navigation -AutomationId $target.Navigation
        Invoke-AutomationElement -Element $navigation
        Find-AutomationElement -Root $root -AutomationId $target.Page | Out-Null
    }

    if (-not [TaskFlyoutSmokeNativeMethods]::SetWindowPos(
        [TaskFlyoutSmokeNativeMethods]::FindMainWindow($process.Id), [IntPtr]::Zero, 40, 40, 640, 700, 0x0040)) {
        throw "The main window could not be resized for narrow-layout verification."
    }
    Start-Sleep -Milliseconds 750
    $root = Get-RootElement -Process $process
    Find-AutomationElement -Root $root -AutomationId "CalendarToggleAccounts" | Out-Null
    $collapseAccounts = Find-AutomationElement -Root $root -AutomationId "CalendarCollapseAccounts" -Optional
    if ($null -ne $collapseAccounts -and -not $collapseAccounts.Current.IsOffscreen) {
        throw "Calendar account pane remained visible in the initial narrow layout."
    }

    $toggle = Find-AutomationElement -Root $root -AutomationId "CalendarToggleAccounts"
    Invoke-AutomationElement -Element $toggle
    Start-Sleep -Milliseconds 500
    $root = Get-RootElement -Process $process
    $collapseAccounts = Find-AutomationElement -Root $root -AutomationId "CalendarCollapseAccounts"
    if ($collapseAccounts.Current.IsOffscreen) {
        throw "Calendar account pane was not keyboard-invokable in narrow layout."
    }

    [System.Windows.Forms.SendKeys]::SendWait("%{F4}")
    Start-Sleep -Seconds 1
    $process.Refresh()
    if ($process.HasExited) {
        throw "Closing the main window terminated the tray process."
    }
    $process = Start-TestApplication -PackageFamilyName $package.PackageFamilyName
    $root = Get-RootElement -Process $process
    Find-AutomationElement -Root $root -AutomationId "MainNavigation" | Out-Null

    Write-Host "Packaged smoke checks passed for $($package.PackageFullName)." -ForegroundColor Green
}
finally {
    Get-Process -Name "Task_Flyout" -ErrorAction SilentlyContinue | Stop-Process -Force
}
