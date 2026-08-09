[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [switch]$SkipProbe,

    [switch]$RequireProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio locator not found: $vswhere"
}

$visualStudio = & $vswhere `
    -latest `
    -products '*' `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -property installationPath
if (-not $visualStudio) {
    throw 'Visual Studio with the x64 C++ toolchain is required.'
}
$visualStudio = $visualStudio.Trim()

$cmake = Join-Path $visualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $cmake)) {
    throw "Bundled CMake was not found: $cmake"
}

$msvcRoot = Join-Path $visualStudio 'VC\Tools\MSVC'
$msvc = Get-ChildItem -LiteralPath $msvcRoot -Directory |
    Sort-Object Name -Descending |
    Select-Object -First 1
if (-not $msvc) {
    throw "MSVC tools were not found: $msvcRoot"
}
$dumpbin = Join-Path $msvc.FullName 'bin\Hostx64\x64\dumpbin.exe'
if (-not (Test-Path -LiteralPath $dumpbin)) {
    throw "dumpbin was not found: $dumpbin"
}
$vcvars = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) {
    throw "x64 Visual C++ environment script was not found: $vcvars"
}

$sourceDirectory = Join-Path $PSScriptRoot '..\native\taskbar-weather'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\.testbuild\native-taskbar-weather'
}
$sourceDirectory = [System.IO.Path]::GetFullPath($sourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

function Invoke-CMakeInNativeEnvironment {
    param([Parameter(Mandatory)][string]$Arguments)

    $command =
        "call `"$vcvars`" >nul && `"$cmake`" $Arguments"
    & cmd.exe /d /c $command
    if ($LASTEXITCODE -ne 0) {
        throw "Native CMake command failed with exit code $LASTEXITCODE."
    }
}

Invoke-CMakeInNativeEnvironment (
    "-S `"$sourceDirectory`" -B `"$OutputDirectory`" -G Ninja " +
    "-DCMAKE_BUILD_TYPE=$Configuration")
Invoke-CMakeInNativeEnvironment (
    "--build `"$OutputDirectory`" --parallel")
Invoke-CMakeInNativeEnvironment (
    "--build `"$OutputDirectory`" --target test")

$configurationDirectory = Join-Path $OutputDirectory $Configuration
$binaryDirectory = if (Test-Path -LiteralPath $configurationDirectory) {
    $configurationDirectory
} else {
    $OutputDirectory
}
$hostBinary = Join-Path $binaryDirectory 'TaskFlyout.TaskbarHost.dll'
$broker = Join-Path $binaryDirectory 'TaskFlyout.TaskbarBroker.exe'
foreach ($binary in @($hostBinary, $broker)) {
    if (-not (Test-Path -LiteralPath $binary)) {
        throw "Expected native binary was not produced: $binary"
    }

    $imports = (& $dumpbin /nologo /imports $binary) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to inspect native imports: $binary"
    }
    if ($imports -match '(?i)WebView|EmbeddedBrowser|winhttp\.dll|wininet\.dll|urlmon\.dll') {
        throw "Forbidden web/network dependency found in native taskbar binary: $binary"
    }
}

if (-not $SkipProbe) {
    $probeOutput = (& $broker probe --strict 2>&1) -join "`n"
    $probeExitCode = $LASTEXITCODE
    Write-Output $probeOutput
    if ($probeExitCode -ne 0) {
        $desktopUnavailable =
            $probeOutput -match '"status":"taskbar-window-missing"' -or
            $probeOutput -match '"status":"taskbar-owner-unavailable"' -or
            $probeOutput -match '"status":"session-mismatch"'
        if (-not $RequireProbe -and $desktopUnavailable) {
            Write-Warning 'The current sandbox cannot see the interactive taskbar; compatibility probe was skipped.'
        } else {
            throw "Current taskbar profile is not supported (exit $probeExitCode)."
        }
    }
}

Get-Item -LiteralPath $hostBinary, $broker |
    Select-Object Name, Length, FullName
