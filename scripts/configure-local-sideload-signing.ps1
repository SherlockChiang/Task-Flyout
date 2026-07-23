<#
.SYNOPSIS
    Configures local SideloadOnly MSIX builds to sign with the current-user certificate.
#>

param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\Package.appxmanifest"),
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\Directory.Build.local.props"),
    [switch]$TrustMachine
)

$ErrorActionPreference = "Stop"

$manifest = (Resolve-Path -LiteralPath $ManifestPath).Path
[xml]$manifestXml = Get-Content -LiteralPath $manifest
$publisher = $manifestXml.Package.Identity.Publisher
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
    throw "No valid current-user signing certificate matches manifest publisher '$publisher'. Run sign-sideload-package.ps1 once to create it."
}

if ($TrustMachine) {
    $trusted = Get-ChildItem Cert:\LocalMachine\TrustedPeople -ErrorAction SilentlyContinue |
        Where-Object Thumbprint -eq $certificate.Thumbprint |
        Select-Object -First 1
    if ($null -eq $trusted) {
        $publicPath = Join-Path $env:TEMP "task-flyout-$($certificate.Thumbprint).cer"
        Export-Certificate -Cert $certificate -FilePath $publicPath -Force | Out-Null
        try {
            $process = Start-Process certutil.exe `
                -ArgumentList @("-f", "-addstore", "TrustedPeople", "`"$publicPath`"") `
                -Verb RunAs -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw "Machine trust was not granted." }
        }
        finally {
            Remove-Item -LiteralPath $publicPath -Force -ErrorAction SilentlyContinue
        }
    }
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$parent = Split-Path -Parent $output
if (-not (Test-Path -LiteralPath $parent)) {
    throw "Output parent directory does not exist: $parent"
}

$props = @"
<Project>
  <PropertyGroup Condition="'`$(UapAppxPackageBuildMode)' == 'SideloadOnly'">
    <AppxPackageSigningEnabled>true</AppxPackageSigningEnabled>
    <PackageCertificateThumbprint>$($certificate.Thumbprint)</PackageCertificateThumbprint>
  </PropertyGroup>
</Project>
"@
[System.IO.File]::WriteAllText($output, $props, [System.Text.UTF8Encoding]::new($false))

Write-Host "Configured local SideloadOnly signing: $output" -ForegroundColor Green
Write-Host "Certificate: $($certificate.Subject) [$($certificate.Thumbprint)]" -ForegroundColor Green
