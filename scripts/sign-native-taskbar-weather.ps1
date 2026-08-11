<#
.SYNOPSIS
    Signs the standalone taskbar broker and Explorer host with an existing
    current-user certificate matching Package.appxmanifest.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BinaryDirectory,
    [string]$ManifestPath,
    [string]$CertificateThumbprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$binaryDirectory = (Resolve-Path -LiteralPath $BinaryDirectory).Path
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot "..\Package.appxmanifest"
}
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
[xml]$manifestXml = Get-Content -LiteralPath $manifest
$publisher = $manifestXml.Package.Identity.Publisher
if ([string]::IsNullOrWhiteSpace($publisher)) {
    throw "Package publisher is missing from $manifest."
}

$store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::My,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
try {
    $store.Open(
        [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    $certificateCandidates = @($store.Certificates | Where-Object {
        $_.Subject -eq $publisher -and
        $_.HasPrivateKey -and
        $_.NotBefore -le (Get-Date) -and
        $_.NotAfter -gt (Get-Date).AddDays(1)
    })
}
finally {
    $store.Close()
}
if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $certificate = $certificateCandidates |
        Where-Object Thumbprint -eq $normalizedThumbprint |
        Select-Object -First 1
} else {
    $certificate = $certificateCandidates |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1
}
if ($null -eq $certificate) {
    $selection = if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        "publisher '$publisher'"
    } else {
        "publisher '$publisher' and thumbprint '$CertificateThumbprint'"
    }
    throw "No valid current-user signing certificate matches $selection."
}

$signTool = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" `
    -Recurse -File -Filter 'signtool.exe' |
    Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signTool) {
    throw 'Windows SDK signtool.exe was not found.'
}

$binaries = @(
    (Join-Path $binaryDirectory 'TaskFlyout.TaskbarBroker.exe'),
    (Join-Path $binaryDirectory 'TaskFlyout.TaskbarHost.dll')
)
foreach ($binary in $binaries) {
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw "Expected standalone taskbar binary was not produced: $binary"
    }

    & $signTool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $binary
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for $binary."
    }

    & $signTool.FullName verify /pa /v $binary
    if ($LASTEXITCODE -ne 0) {
        throw "Signature verification failed for $binary."
    }
}

Write-Host "Signed standalone taskbar binaries with $($certificate.Thumbprint)." -ForegroundColor Green
