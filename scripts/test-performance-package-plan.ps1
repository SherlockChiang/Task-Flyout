param()

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$builder = Join-Path $PSScriptRoot 'build-performance-control.ps1'
$thumbprint = '15303040F7CEECFDE9C023F15481D15C39CCDB46'
$sourceManifest = Join-Path $root 'Package.appxmanifest'
$originalHash = (Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash
$checks = 0

function Assert-Check([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

$plan = & $builder -Version '1.4.4.5' -CertificateThumbprint $thumbprint -DescribeOnly
Assert-Check ($plan.Version -eq '1.4.4.5' -and $plan.Configuration -eq 'Release') 'Wrong control configuration.'
Assert-Check (-not $plan.InstallsPackage -and -not $plan.ChangesTrust -and -not $plan.SourceManifestChanged) 'Unexpected side effects in the plan.'
Assert-Check ($plan.SourceCommit -match '^[a-f0-9]{40}$') 'Missing exact source commit.'
foreach ($version in @('1.4.4', '1.4.4.65536', '1.4.4.-1', '1.4.4.5.6', 'invalid')) {
    $rejected = $false
    try { & $builder -Version $version -CertificateThumbprint $thumbprint -DescribeOnly | Out-Null }
    catch { $rejected = $true }
    Assert-Check $rejected "Invalid version accepted: $version"
}
$badThumbprint = $false
try { & $builder -Version '1.4.4.5' -CertificateThumbprint 'invalid' -DescribeOnly | Out-Null }
catch { $badThumbprint = $true }
Assert-Check $badThumbprint 'Invalid thumbprint accepted.'

# Evaluation only: no build, signing, installation or certificate access.
$directory = Join-Path $root ('.testbuild\performance-package-plan-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($directory)
$alternate = Join-Path $directory 'Package.appxmanifest'
try {
    [xml]$xml = Get-Content -LiteralPath $sourceManifest
    $xml.Package.Identity.Version = '1.4.4.5'
    $xml.Save($alternate)
    $project = Join-Path $root 'Task_Flyout.csproj'
    $evaluated = & dotnet msbuild $project /nologo /getItem:AppxManifest /getProperty:_IsMsixCustomBeforeMicrosoftCommonTargetsChainValid,_IsWinUICustomBeforeMicrosoftCommonTargetsChainValid /p:Configuration=Release /p:Platform=x64 "/p:TaskFlyoutPackageManifest=$alternate"
    if ($LASTEXITCODE -ne 0) { throw 'Alternate manifest evaluation failed.' }
    $evaluation = ($evaluated -join [Environment]::NewLine) | ConvertFrom-Json
    $items = @($evaluation.Items.AppxManifest)
    Assert-Check ($items.Count -eq 1 -and $items[0].Identity -eq $alternate) 'Alternate manifest was not selected exactly once.'
    Assert-Check ($evaluation.Properties._IsMsixCustomBeforeMicrosoftCommonTargetsChainValid -eq 'true' -and $evaluation.Properties._IsWinUICustomBeforeMicrosoftCommonTargetsChainValid -eq 'true') 'SDK import chain was bypassed.'
    $evaluated = & dotnet msbuild $project /nologo /getItem:AppxManifest /p:Configuration=Release /p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw 'Default manifest evaluation failed.' }
    $items = @((($evaluated -join [Environment]::NewLine) | ConvertFrom-Json).Items.AppxManifest)
    Assert-Check ($items.Count -eq 1 -and $items[0].Identity -eq 'Package.appxmanifest') 'Normal manifest selection changed.'
}
finally {
    Remove-Item -LiteralPath $alternate -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $directory -ErrorAction SilentlyContinue
}
Assert-Check ((Get-FileHash -LiteralPath $sourceManifest -Algorithm SHA256).Hash -eq $originalHash) 'Source manifest changed.'
Write-Output "Performance package planning checks passed: $checks (no build/sign/install)."
