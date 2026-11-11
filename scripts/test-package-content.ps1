[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$PackageDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$trackedSensitiveFiles = @(
    'credentials.json',
    'Secrets.cs',
    'Task_Flyout_TemporaryKey.pfx'
)
foreach ($relativePath in $trackedSensitiveFiles) {
    git ls-files --error-unmatch -- $relativePath *> $null
    $gitExitCode = $LASTEXITCODE
    if ($gitExitCode -eq 0) {
        throw "Sensitive build input is tracked by Git: $relativePath"
    }
    if ($gitExitCode -ne 1) {
        throw "Could not inspect Git tracking state for sensitive build input: $relativePath (exit code $gitExitCode)."
    }
}

if (-not [string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $resolvedPackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $forbiddenNames = @(
        'credentials.json',
        'Secrets.cs',
        '*.pfx',
        '*.p12'
    )
    foreach ($pattern in $forbiddenNames) {
        $forbidden = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Recurse -File -Filter $pattern)
        if ($forbidden.Count -gt 0) {
            throw "Forbidden sensitive file found in package output: $($forbidden[0].FullName)"
        }
    }
}

Write-Host 'Release input and package content checks passed.' -ForegroundColor Green
$global:LASTEXITCODE = 0
