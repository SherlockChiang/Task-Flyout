<#
.SYNOPSIS
    Signs and installs the newest locally packaged Task Flyout MSIX.
#>

param(
    [string]$AppPackagesPath = (Join-Path $PSScriptRoot "..\AppPackages"),
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

$packageRoot = (Resolve-Path -LiteralPath $AppPackagesPath).Path
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File -Filter "*.msix" |
    Where-Object {
        $_.DirectoryName -notmatch "[\\/]Dependencies([\\/]|$)" -and
        $_.Name -match "Task_Flyout_.*_$Platform\.msix$"
    } |
    Sort-Object LastWriteTime -Descending)
if ($packages.Count -eq 0) {
    throw "No locally packaged Task Flyout $Platform MSIX was found under $packageRoot."
}

$package = $packages[0]
Write-Host "Using newest package: $($package.FullName)" -ForegroundColor Cyan

& (Join-Path $PSScriptRoot "sign-sideload-package.ps1") `
    -PackagePath $package.FullName `
    -TrustMachine
if ($LASTEXITCODE -ne 0) {
    throw "Package signing failed."
}

$signature = Get-AuthenticodeSignature -LiteralPath $package.FullName
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Package signature is not valid: $($signature.StatusMessage)"
}

$dependency = Get-ChildItem -LiteralPath (Join-Path $package.DirectoryName "Dependencies\$Platform") `
    -File -Filter "Microsoft.WindowsAppRuntime*.msix" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

Get-Process -Name "Task_Flyout" -ErrorAction SilentlyContinue | Stop-Process -Force

$installArguments = @{
    Path = $package.FullName
    ForceUpdateFromAnyVersion = $true
}
if ($null -ne $dependency) {
    $installArguments.DependencyPath = $dependency.FullName
}
Add-AppxPackage @installArguments

$installed = Get-AppxPackage -Name "Uranus92.TaskFlyout" |
    Where-Object Publisher -eq $signature.SignerCertificate.Subject |
    Sort-Object Version -Descending |
    Select-Object -First 1
if ($null -eq $installed -or $installed.Status -ne "Ok") {
    throw "The signed Task Flyout package was not installed successfully."
}

Write-Host "Installed: $($installed.PackageFullName)" -ForegroundColor Green
