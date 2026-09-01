<#
.SYNOPSIS
    Signs the standalone taskbar broker and Explorer host with an existing
    current-user certificate matching Package.appxmanifest, or explicitly
    creates and trusts a replacement when the matching private key is missing.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BinaryDirectory,
    [string]$ManifestPath,
    [string]$CertificateThumbprint,
    [switch]$CreateAndTrustCertificateIfMissing
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($CreateAndTrustCertificateIfMissing -and
    -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw (
        'CreateAndTrustCertificateIfMissing cannot recreate a requested ' +
        'thumbprint. Omit CertificateThumbprint to create a new ' +
        'certificate for the manifest publisher.')
}

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

function Test-CertificatePrivateKeyAvailable {
    param(
        [Parameter(Mandatory)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]
        $Certificate
    )

    $privateKey = $null
    try {
        $privateKey =
            [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey(
                $Certificate)
        return $null -ne $privateKey
    }
    catch {
        return $false
    }
    finally {
        if ($null -ne $privateKey) {
            $privateKey.Dispose()
        }
    }
}

function Test-CertificateSupportsCodeSigning {
    param(
        [Parameter(Mandatory)]
        [System.Security.Cryptography.X509Certificates.X509Certificate2]
        $Certificate
    )

    $codeSigningOid = '1.3.6.1.5.5.7.3.3'
    foreach ($extension in $Certificate.Extensions) {
        if ($extension.Oid.Value -ne '2.5.29.37') {
            continue
        }

        $enhancedKeyUsage =
            [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new(
                $extension,
                $extension.Critical)
        return $null -ne ($enhancedKeyUsage.EnhancedKeyUsages |
            Where-Object Value -eq $codeSigningOid |
            Select-Object -First 1)
    }

    return $false
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
        $_.NotAfter -gt (Get-Date).AddDays(1) -and
        (Test-CertificatePrivateKeyAvailable -Certificate $_) -and
        (Test-CertificateSupportsCodeSigning -Certificate $_)
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
    if ($CreateAndTrustCertificateIfMissing) {
        Write-Warning (
            'Creating and trusting a new non-exportable current-user ' +
            "code-signing certificate for $publisher.")
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $publisher `
            -FriendlyName 'Task Flyout local sideload signing' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyUsage DigitalSignature `
            -KeyExportPolicy NonExportable `
            -NotAfter (Get-Date).AddYears(2) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
    }
}
if ($null -eq $certificate) {
    $selection = if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        "publisher '$publisher'"
    } else {
        "publisher '$publisher' and thumbprint '$CertificateThumbprint'"
    }
    throw "No valid current-user signing certificate matches $selection."
}

$now = Get-Date
if ($certificate.Subject -ne $publisher -or
    -not $certificate.HasPrivateKey -or
    $certificate.NotBefore -gt $now -or
    $certificate.NotAfter -le $now.AddDays(1) -or
    -not (Test-CertificatePrivateKeyAvailable -Certificate $certificate) -or
    -not (Test-CertificateSupportsCodeSigning -Certificate $certificate)) {
    throw (
        'The selected signing certificate failed publisher, validity, ' +
        'private-key, or code-signing-purpose validation.')
}

if ($CreateAndTrustCertificateIfMissing) {
    foreach ($storeName in @('TrustedPeople', 'Root')) {
        $trustStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
            $storeName,
            [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        try {
            $trustStore.Open(
                [System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $alreadyTrusted = $trustStore.Certificates.Find(
                [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $certificate.Thumbprint,
                $false)
            if ($alreadyTrusted.Count -eq 0) {
                $trustStore.Add($certificate)
            }
        }
        finally {
            $trustStore.Close()
        }
    }
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
