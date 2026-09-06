<#
.SYNOPSIS
Builds and signs a local Release control without editing the source manifest or installing it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$CertificateThumbprint,
    [switch]$DescribeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$manifestPath = Join-Path $root 'Package.appxmanifest'
[xml]$manifest = Get-Content -LiteralPath $manifestPath
[version]$packageVersion = $Version
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
    @($packageVersion.Major, $packageVersion.Minor, $packageVersion.Build, $packageVersion.Revision | Where-Object { $_ -lt 0 -or $_ -gt 65535 }).Count -ne 0) {
    throw 'Version must contain four components between 0 and 65535.'
}
$Version = $packageVersion.ToString(4)
$source = & git -C $root rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the source commit.' }
$source = $source.Trim()
$output = Join-Path $root "AppPackages\PerformanceControl_${Version}_$($source.Substring(0, 7))"
if ($DescribeOnly) {
    [pscustomobject]@{
        SourceCommit = $source; Version = $Version; Configuration = 'Release'
        OutputDirectory = $output; CertificateThumbprint = $CertificateThumbprint
        SourceManifestChanged = $false; InstallsPackage = $false; ChangesTrust = $false
    }
    return
}

$allowedChanges = @('Package.appxmanifest', 'windhawk/task-flyout-weather-companion.wh.cpp')
$changes = @(& git -C $root diff --name-only HEAD)
if ($LASTEXITCODE -ne 0) { throw 'Cannot check source changes.' }
if (@($changes | Where-Object { $_ -notin $allowedChanges }).Count) {
    throw 'Commit implementation/tooling changes before building the control.'
}
$untrackedSources = @(& git -C $root ls-files --others --exclude-standard -- '*.cs' '*.xaml' '*.csproj' '*.props' '*.targets' '*.appxmanifest')
if ($LASTEXITCODE -ne 0 -or $untrackedSources.Count) { throw 'Untracked build inputs must be resolved before packaging.' }
if (Test-Path -LiteralPath $output) { throw "Output already exists; select a new version: $output" }

$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
if ($certificate.Subject -ne $manifest.Package.Identity.Publisher -or -not $certificate.HasPrivateKey -or
    $certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date).AddDays(1)) {
    throw 'The existing certificate does not match the publisher, validity window or private-key requirement.'
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = @(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\amd64\MSBuild.exe') | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or -not $msbuild) { throw 'Visual Studio x64 MSBuild was not found.' }
$signTool = Get-ChildItem -LiteralPath "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -File -Filter signtool.exe |
    Where-Object FullName -match '\\x64\\signtool\.exe$' | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) { throw 'Windows SDK x64 SignTool was not found.' }

$preserved = @($allowedChanges | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $root $_) -Algorithm SHA256 })
$staging = Join-Path $root ('.testbuild\performance-control-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($staging)
$stagedManifest = Join-Path $staging 'Package.appxmanifest'
$manifest.Package.Identity.Version = $Version
$manifest.Save($stagedManifest)
$arguments = @(
    (Join-Path $root 'Task_Flyout.csproj'), '/nologo', '/v:minimal', '/m'
    '/p:Configuration=Release', '/p:Platform=x64', '/p:RuntimeIdentifier=win-x64'
    '/p:RestoreLockedMode=true', '/p:SelfContained=true', '/p:GenerateAppxPackageOnBuild=true'
    '/p:AppxBundle=Never', '/p:UapAppxPackageBuildMode=SideloadOnly'
    '/p:AppxPackageSigningEnabled=false', '/p:SignStandaloneTaskbarNativeArtifacts=true'
    "/p:PackageCertificateThumbprint=$CertificateThumbprint"
    "/p:AppxPackageDir=$output\", "/p:TaskFlyoutPackageManifest=$stagedManifest"
)
try {
    # Validate the evaluated manifest item before allowing a build/sign operation.
    $evaluated = & $msbuild @arguments /getItem:AppxManifest
    if ($LASTEXITCODE -ne 0) { throw 'Manifest override evaluation failed.' }
    $items = @((($evaluated -join [Environment]::NewLine) | ConvertFrom-Json).Items.AppxManifest)
    if ($items.Count -ne 1 -or [IO.Path]::GetFullPath($items[0].Identity) -ne $stagedManifest) {
        throw 'MSBuild did not select the isolated package manifest.'
    }
    & $msbuild @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Release control build failed.' }
    $packages = @(Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.msix' |
        Where-Object { $_.DirectoryName -notmatch '[\\/]Dependencies([\\/]|$)' })
    if ($packages.Count -ne 1) { throw 'Expected exactly one application MSIX.' }
    $package = $packages[0].FullName
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw 'The package manifest is missing.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$packagedManifest = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        $identity = $packagedManifest.Package.Identity
        if ($identity.Name -ne $manifest.Package.Identity.Name -or
            $identity.Publisher -ne $certificate.Subject -or
            $identity.Version -ne $Version -or $identity.ProcessorArchitecture -ne 'x64') {
            throw 'Packaged name, publisher, version or architecture does not match the control.'
        }
    }
    finally { $archive.Dispose() }
    & $signTool.FullName sign /fd SHA256 /sha1 $CertificateThumbprint /s My $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    & $signTool.FullName verify /pa /v $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $package
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
        throw 'The MSIX signature did not validate against the requested certificate.'
    }
    [pscustomobject]@{
        SourceCommit = $source; Version = $Version; Configuration = 'Release'
        PackagePath = $package; SHA256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash
        CertificateThumbprint = $CertificateThumbprint; Installed = $false
    }
}
finally {
    foreach ($file in $preserved) {
        if ((Get-FileHash -LiteralPath $file.Path -Algorithm SHA256).Hash -ne $file.Hash) {
            Write-Error "Pre-existing source changed during packaging; it has not been reverted: $($file.Path)"
        }
    }
}
