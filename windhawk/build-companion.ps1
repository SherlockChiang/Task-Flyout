[CmdletBinding()]
param(
    [ValidateSet('Syntax', 'Link')]
    [string]$Mode = 'Link',

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$windhawkRoot = Join-Path $env:ProgramFiles 'Windhawk'
$compilerRoot = Join-Path $windhawkRoot 'Compiler'
$compiler = Join-Path $compilerRoot 'bin\clang++.exe'
$windhawkIni = Join-Path $windhawkRoot 'windhawk.ini'
$source = Join-Path $PSScriptRoot 'task-flyout-weather-companion.wh.cpp'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Windhawk compiler not found: $compiler"
}

if (-not (Test-Path -LiteralPath $windhawkIni)) {
    throw "Windhawk configuration not found: $windhawkIni"
}

$enginePathSetting = Get-Content -LiteralPath $windhawkIni |
    Where-Object { $_ -match '^EnginePath=(.+)$' } |
    Select-Object -First 1
if (-not $enginePathSetting) {
    throw 'EnginePath is missing from windhawk.ini.'
}

$enginePath = ($enginePathSetting -replace '^EnginePath=', '').Trim()
if (-not [System.IO.Path]::IsPathRooted($enginePath)) {
    $enginePath = Join-Path $windhawkRoot $enginePath
}
$engineLibrary = Join-Path $enginePath '64\windhawk.lib'
if (-not (Test-Path -LiteralPath $engineLibrary)) {
    throw "Windhawk engine import library not found: $engineLibrary"
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\.testbuild\windhawk'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$commonArguments = @(
    '-std=c++23',
    '-DUNICODE',
    '-D_UNICODE',
    '-DWINVER=0x0A00',
    '-D_WIN32_WINNT=0x0A00',
    '-D_WIN32_IE=0x0A00',
    '-DNTDDI_VERSION=0x0A000008',
    '-D__USE_MINGW_ANSI_STDIO=0',
    '-DWH_MOD',
    '-DWH_MOD_ID=L\"task-flyout-weather-companion\"',
    '-DWH_MOD_VERSION=L\"0.1.0\"'
)

if ($Mode -eq 'Syntax') {
    $compilerArguments = @($commonArguments) + @(
        '-x',
        'c++',
        $source,
        '-include',
        'windhawk_api.h',
        '-target',
        'x86_64-w64-mingw32',
        '-fsyntax-only',
        '-Wno-pragma-pack',
        '-Wno-pragma-system-header-outside-header'
    )
} else {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $output = Join-Path $OutputDirectory 'task-flyout-weather-companion.dll'
    $compilerArguments = @('-O2', '-shared') + @($commonArguments) + @(
        $engineLibrary,
        '-x',
        'c++',
        $source,
        '-include',
        'windhawk_api.h',
        '-target',
        'x86_64-w64-mingw32',
        '-Wl,--export-all-symbols',
        '-o',
        $output,
        '-lole32',
        '-loleaut32',
        '-lruntimeobject'
    )
}

Push-Location $compilerRoot
try {
    & $compiler @compilerArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windhawk companion compilation failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}

if ($Mode -eq 'Syntax') {
    Write-Output 'Windhawk companion syntax check passed.'
} else {
    Get-Item -LiteralPath $output
}
