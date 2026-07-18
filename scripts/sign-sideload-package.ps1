<#
.SYNOPSIS
    Signs a local Task Flyout MSIX for sideload installation.

.DESCRIPTION
    Creates or reuses a current-user code-signing certificate whose subject
    matches Package.appxmanifest, trusts it in the current-user stores, then
    signs and verifies the selected MSIX. This certificate is for local testing
    only and must not be used for public releases.
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\Package.appxmanifest"),
    [switch]$TrustMachine
)

$ErrorActionPreference = "Stop"

$package = (Resolve-Path -LiteralPath $PackagePath).Path
$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
if ([System.IO.Path]::GetExtension($package) -notin @(".msix", ".appx")) {
    throw "PackagePath must point to an .msix or .appx file."
}

[xml]$manifestXml = Get-Content -LiteralPath $manifest
$publisher = $manifestXml.Package.Identity.Publisher
if ([string]::IsNullOrWhiteSpace($publisher)) {
    throw "Package publisher is missing from $manifest."
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object {
        $_.Subject -eq $publisher -and
        $_.HasPrivateKey -and
        $_.NotBefore -le (Get-Date) -and
        $_.NotAfter -gt (Get-Date).AddDays(1)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $publisher `
        -FriendlyName "Task Flyout local sideload signing" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(2) `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
}

foreach ($storeName in @("TrustedPeople", "Root")) {
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $storeName,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        if (-not $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $certificate.Thumbprint,
            $false)) {
            $store.Add($certificate)
        }
    }
    finally {
        $store.Close()
    }
}

if ($TrustMachine) {
    $machineStore = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $certificate.Thumbprint |
        Select-Object -First 1
    if ($null -eq $machineStore) {
        $publicCertificatePath = Join-Path $env:TEMP "task-flyout-$($certificate.Thumbprint).cer"
        Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
        try {
            $process = Start-Process -FilePath "certutil.exe" `
                -ArgumentList @("-f", "-addstore", "TrustedPeople", "`"$publicCertificatePath`"") `
                -Verb RunAs `
                -Wait `
                -PassThru
            if ($process.ExitCode -ne 0) {
                throw "Machine certificate trust was not granted."
            }
        }
        finally {
            Remove-Item -LiteralPath $publicCertificatePath -Force -ErrorAction SilentlyContinue
        }
    }
}

$signTool = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -File -Filter "signtool.exe" |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -eq $signTool) {
    throw "Windows SDK signtool.exe was not found."
}

& $signTool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $package
if ($LASTEXITCODE -ne 0) {
    throw "Signing failed. Verify that the certificate subject matches the manifest publisher: $publisher"
}

& $signTool.FullName verify /pa /v $package
if ($LASTEXITCODE -ne 0) {
    throw "Signature verification failed for $package."
}

$signature = Get-AuthenticodeSignature -LiteralPath $package
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "PowerShell signature verification returned $($signature.Status): $($signature.StatusMessage)"
}

Write-Host "Signed package: $package" -ForegroundColor Green
Write-Host "Certificate: $($certificate.Subject) [$($certificate.Thumbprint)]" -ForegroundColor Green
