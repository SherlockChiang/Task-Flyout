<#
.SYNOPSIS
    Runs the Task Flyout long-cycle improvement harness.

.DESCRIPTION
    Performs the repeatable checks used after small optimization batches:
    refresh Git index metadata, verify the tree has no content diff, build the
    WinUI app, run pure logic tests, and write a timestamped log.
#>

param(
    [string]$Configuration = "Debug",
    [string]$Platform = "x64",
    [string]$LogRoot = ".harness\runs",
    [switch]$AllowDirty,
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

$runId = Get-Date -Format "yyyyMMdd-HHmmss"
$runDir = Join-Path $repoRoot (Join-Path $LogRoot $runId)
New-Item -ItemType Directory -Path $runDir -Force | Out-Null
$logPath = Join-Path $runDir "harness.log"
$summaryPath = Join-Path $runDir "summary.txt"

$script:Failed = $false
$script:StepResults = New-Object System.Collections.Generic.List[string]

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
    $line | Tee-Object -FilePath $logPath -Append | Out-Host
}

function Invoke-HarnessCommand {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments = @()
    )

    Write-Log "START $Name"
    Write-Log ("CMD {0} {1}" -f $Command, ($Arguments -join " "))

    $output = & $Command @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Log $line.ToString()
    }

    if ($exitCode -ne 0) {
        Write-Log "FAIL $Name exit=$exitCode"
        $script:StepResults.Add("FAIL $Name exit=$exitCode")
        $script:Failed = $true
        return $false
    }

    Write-Log "PASS $Name"
    $script:StepResults.Add("PASS $Name")
    return $true
}

function Test-GitClean {
    param([string]$Name)

    Write-Log "START $Name"
    & git update-index -q --refresh

    & git diff --quiet
    $workTreeDirty = $LASTEXITCODE -ne 0

    & git diff --cached --quiet
    $indexDirty = $LASTEXITCODE -ne 0

    $status = & git status --short
    if ($status) {
        Write-Log "git status --short:"
        foreach ($line in $status) { Write-Log $line }
    }

    if (($workTreeDirty -or $indexDirty) -and -not $AllowDirty) {
        Write-Log "FAIL $Name content diff detected. Re-run with -AllowDirty to permit dirty trees."
        $script:StepResults.Add("FAIL $Name content diff detected")
        $script:Failed = $true
        return $false
    }

    if ($workTreeDirty -or $indexDirty) {
        Write-Log "WARN $Name content diff allowed by -AllowDirty"
        $script:StepResults.Add("WARN $Name dirty allowed")
        return $true
    }

    Write-Log "PASS $Name no content diff"
    $script:StepResults.Add("PASS $Name")
    return $true
}

Write-Log "Task Flyout harness run $runId"
Write-Log "Repo: $repoRoot"
Write-Log "Configuration: $Configuration"
Write-Log "Platform: $Platform"

Invoke-HarnessCommand "dotnet sdk" "dotnet" @("--version") | Out-Null
Test-GitClean "preflight git clean" | Out-Null

if (-not $SkipBuild) {
    Invoke-HarnessCommand "app build" "dotnet" @(
        "build",
        "Task_Flyout.csproj",
        "-c",
        $Configuration,
        "-p:Platform=$Platform"
    ) | Out-Null
}
else {
    Write-Log "SKIP app build"
    $script:StepResults.Add("SKIP app build")
}

if (-not $SkipTests) {
    Invoke-HarnessCommand "unit tests" "dotnet" @(
        "test",
        "Tests\Task_Flyout.Tests\Task_Flyout.Tests.csproj",
        "-c",
        $Configuration
    ) | Out-Null
}
else {
    Write-Log "SKIP unit tests"
    $script:StepResults.Add("SKIP unit tests")
}

Test-GitClean "postflight git clean" | Out-Null

$result = if ($script:Failed) { "FAIL" } else { "PASS" }
Write-Log "RESULT $result"
Write-Log "Log: $logPath"

$summary = @(
    "Task Flyout harness run $runId",
    "Result: $result",
    "Configuration: $Configuration",
    "Platform: $Platform",
    "Log: $logPath",
    "",
    "Steps:"
) + $script:StepResults

$summary | Set-Content -LiteralPath $summaryPath -Encoding UTF8

if ($script:Failed) {
    exit 1
}

exit 0
