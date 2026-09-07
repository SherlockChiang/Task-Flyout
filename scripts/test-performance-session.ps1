param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'start-performance-session.ps1'
$checks = 0
function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseFile($scriptPath, [ref]$tokens, [ref]$errors)
Assert-Check ($errors.Count -eq 0) 'Launcher syntax is invalid.'

foreach ($values in @(
    @{ExpectedVersion='invalid'}, @{ExpectedBuildId='invalid'},
    @{ExpectedAssemblySha256='invalid'}, @{LogTimeoutSeconds=0}, @{LogTimeoutSeconds=61}
)) {
    $rejected = $false
    try { & $scriptPath @values -DescribeOnly | Out-Null } catch { $rejected = $true }
    Assert-Check $rejected 'Invalid launch input was accepted.'
}

$beforeIds = @(Get-Process -Name Task_Flyout -ErrorAction SilentlyContinue | ForEach-Object Id | Sort-Object)
$oldDiagnosticEnvironment = [Environment]::GetEnvironmentVariable('TASKFLYOUT_PERFORMANCE_DIAGNOSTICS', 'Process')
$oldTaskbarEnvironment = [Environment]::GetEnvironmentVariable('TASKFLYOUT_DISABLE_STANDALONE_TASKBAR', 'Process')
$plan = & $scriptPath -DescribeOnly
Assert-Check ($plan.Version -eq '1.4.4.6' -and $plan.BuildId -eq 'baa8f3b20cd14ae2ab0518a1050732a3') 'Wrong accepted control.'
Assert-Check (-not $plan.ChangesPersistentSettings -and -not $plan.ForceStopsProcesses) 'Unexpected persistent or termination action.'
Assert-Check ($plan.PerformanceLogging -and $plan.StandaloneTaskbarSuppressed) 'Missing scoped diagnostics/archived-experiment guard.'
if ($beforeIds.Count) {
    $rejected = $false
    try { & $scriptPath | Out-Null }
    catch { $rejected = $_.Exception.Message -like 'Task Flyout is still running.*' }
    Assert-Check $rejected 'Launcher did not reject the existing real process.'
    Assert-Check (-not $plan.CanStart) 'Plan failed to report the running process.'
}
else { Write-Output 'No existing process: running-process guard check skipped; no app will be launched by this test.' }
$afterIds = @(Get-Process -Name Task_Flyout -ErrorAction SilentlyContinue | ForEach-Object Id | Sort-Object)
Assert-Check (($beforeIds -join ',') -eq ($afterIds -join ',')) 'Process set changed during read-only/guard tests.'
Assert-Check ([Environment]::GetEnvironmentVariable('TASKFLYOUT_PERFORMANCE_DIAGNOSTICS', 'Process') -ceq $oldDiagnosticEnvironment) 'Parent diagnostics environment changed.'
Assert-Check ([Environment]::GetEnvironmentVariable('TASKFLYOUT_DISABLE_STANDALONE_TASKBAR', 'Process') -ceq $oldTaskbarEnvironment) 'Parent taskbar environment changed.'
Write-Output "Performance launcher preflight passed: $checks checks; no app started/stopped and no sampling performed."
