<#
.SYNOPSIS
    Runs a fail-closed start/readiness/stop check for the standalone taskbar
    Host in an explicitly confirmed disposable Explorer session.

.DESCRIPTION
    This script mutates Explorer. -DescribeOnly is the sole no-mutation path.
    Every attempted start enters a finally block with bounded idempotent stop
    retries; the script never installs a package or restarts Explorer.
#>

[CmdletBinding()]
param(
    [string]$BinaryDirectory,

    [ValidateRange(1, 120)]
    [int]$ReadyTimeoutSeconds = 20,

    [ValidateRange(0, 120)]
    [int]$ReadyHoldSeconds = 15,

    [switch]$AllowExplorerInjection,

    [string]$DisposableSessionConfirmation,

    [switch]$AllowUnsignedDevelopmentBuild,

    [string]$ExpectedSignerThumbprint,

    [switch]$DescribeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredConfirmation = 'TASK_FLYOUT_DISPOSABLE_EXPLORER_SESSION'

if ($DescribeOnly) {
    [pscustomobject]@{
        MutatesExplorer = $true
        InstallsPackage = $false
        RestartsExplorer = $false
        Commands = 'probe --strict; start; bounded status polling; stop; status'
        Cleanup = 'Up to three bounded stop attempts run after every attempted start.'
        RequiredConfirmation = $requiredConfirmation
    } | Format-List
    return
}

if (-not $AllowExplorerInjection -or
    $DisposableSessionConfirmation -cne $requiredConfirmation) {
    throw (
        'This harness injects the standalone Host into Explorer. Run it only ' +
        'in a disposable Explorer session and pass both ' +
        "-AllowExplorerInjection and -DisposableSessionConfirmation " +
        $requiredConfirmation + '.')
}

if ($AllowUnsignedDevelopmentBuild -and
    -not [string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
    throw 'ExpectedSignerThumbprint cannot be combined with AllowUnsignedDevelopmentBuild.'
}

if ([string]::IsNullOrWhiteSpace($BinaryDirectory)) {
    $BinaryDirectory = Join-Path $PSScriptRoot '..\.testbuild\native-taskbar-weather'
}

$resolvedDirectory = (Resolve-Path -LiteralPath $BinaryDirectory).Path
$brokerPath = Join-Path $resolvedDirectory 'TaskFlyout.TaskbarBroker.exe'
$hostPath = Join-Path $resolvedDirectory 'TaskFlyout.TaskbarHost.dll'
foreach ($binary in @($brokerPath, $hostPath)) {
    if (-not (Test-Path -LiteralPath $binary -PathType Leaf)) {
        throw "Expected standalone taskbar binary was not found: $binary"
    }
}

function Normalize-Thumbprint {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }
    return ($Value -replace '\s', '').ToUpperInvariant()
}

function Assert-BinarySignatures {
    if ($AllowUnsignedDevelopmentBuild) {
        Write-Warning (
            'Authenticode validation is disabled for this disposable ' +
            'development run.')
        return
    }

    $signatures = @(
        Get-AuthenticodeSignature -LiteralPath $brokerPath
        Get-AuthenticodeSignature -LiteralPath $hostPath
    )
    foreach ($signature in $signatures) {
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
            $null -eq $signature.SignerCertificate) {
            throw 'Both standalone taskbar binaries must have valid Authenticode signatures.'
        }
    }

    $brokerThumbprint = Normalize-Thumbprint `
        $signatures[0].SignerCertificate.Thumbprint
    $hostThumbprint = Normalize-Thumbprint `
        $signatures[1].SignerCertificate.Thumbprint
    if ($brokerThumbprint -cne $hostThumbprint) {
        throw 'The Broker and Host must be signed by the same certificate.'
    }

    $expected = Normalize-Thumbprint $ExpectedSignerThumbprint
    if (-not [string]::IsNullOrEmpty($expected) -and
        $brokerThumbprint -cne $expected) {
        throw 'The standalone taskbar binaries do not match the expected signer.'
    }
}

function Assert-JsonProperties {
    param(
        [Parameter(Mandatory)]
        [object]$Payload,

        [Parameter(Mandatory)]
        [string[]]$Names
    )

    $available = @($Payload.PSObject.Properties.Name)
    foreach ($name in $Names) {
        if ($available -notcontains $name) {
            throw "Broker JSON is missing required property '$name'."
        }
    }
}

function Invoke-TaskbarBroker {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $lines = @(& $brokerPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($lines | ForEach-Object { $_.ToString() }) -join "`n"
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw 'Broker returned no JSON output.'
    }

    try {
        $payload = ConvertFrom-Json -InputObject $text -ErrorAction Stop
    } catch {
        throw 'Broker returned malformed JSON output.'
    }
    if ($payload -is [System.Array]) {
        throw 'Broker returned more than one JSON result.'
    }
    Assert-JsonProperties -Payload $payload -Names @('status')

    return [pscustomobject]@{
        ExitCode = $exitCode
        Payload = $payload
    }
}

function Assert-ControlResult {
    param(
        [Parameter(Mandatory)]
        [object]$Result,

        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter(Mandatory)]
        [string[]]$AllowedControllerStatus,

        [uint32]$ExpectedProcessId = 0
    )

    Assert-JsonProperties -Payload $Result.Payload -Names @(
        'status',
        'command',
        'processId',
        'controllerStatus')
    if ($Result.ExitCode -ne 0 -or
        $Result.Payload.status -cne 'acknowledged' -or
        $Result.Payload.command -cne $Command) {
        throw "Broker '$Command' was not acknowledged."
    }
    if ($AllowedControllerStatus -notcontains
        [string]$Result.Payload.controllerStatus) {
        throw "Broker '$Command' returned an unexpected controller state."
    }
    if ($ExpectedProcessId -ne 0 -and
        [uint32]$Result.Payload.processId -ne $ExpectedProcessId) {
        throw "Explorer changed while broker '$Command' was running."
    }
}

function Invoke-VerifiedStatus {
    param(
        [Parameter(Mandatory)]
        [uint32]$ExpectedProcessId,

        [Parameter(Mandatory)]
        [string[]]$AllowedControllerStatus
    )

    $result = Invoke-TaskbarBroker -Arguments @(
        'status',
        '--host',
        $hostPath)
    Assert-ControlResult `
        -Result $result `
        -Command 'status' `
        -AllowedControllerStatus $AllowedControllerStatus `
        -ExpectedProcessId $ExpectedProcessId
    return $result
}

function Stop-ControllerWithRetry {
    $lastFailure = 'unknown stop failure'
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $result = Invoke-TaskbarBroker -Arguments @(
                'stop',
                '--host',
                $hostPath)
            Assert-ControlResult `
                -Result $result `
                -Command 'stop' `
                -AllowedControllerStatus @('stopped', 'not-started')
            return $result
        } catch {
            $lastFailure = $_.Exception.Message
            if ($attempt -lt 3) {
                Start-Sleep -Milliseconds 500
            }
        }
    }
    throw "Controller stop was not acknowledged after three attempts: $lastFailure"
}

Assert-BinarySignatures

$probe = Invoke-TaskbarBroker -Arguments @('probe', '--strict')
Assert-JsonProperties -Payload $probe.Payload -Names @(
    'status',
    'processId',
    'threadId',
    'windowsBuild')
if ($probe.ExitCode -ne 0 -or
    $probe.Payload.status -cne 'supported' -or
    [uint32]$probe.Payload.processId -eq 0 -or
    [uint32]$probe.Payload.threadId -eq 0) {
    throw 'The current Explorer taskbar does not match the exact supported profile.'
}
$explorerProcessId = [uint32]$probe.Payload.processId

$null = Invoke-VerifiedStatus `
    -ExpectedProcessId $explorerProcessId `
    -AllowedControllerStatus @('not-started')

$cleanupRequired = $false
$testFailure = $null
$cleanupFailure = $null
try {
    # Set this before invoking start: a timeout or malformed reply is ambiguous
    # and must still take the idempotent stop path.
    $cleanupRequired = $true
    $start = Invoke-TaskbarBroker -Arguments @(
        'start',
        '--host',
        $hostPath)
    Assert-ControlResult `
        -Result $start `
        -Command 'start' `
        -AllowedControllerStatus @('started') `
        -ExpectedProcessId $explorerProcessId
    Assert-JsonProperties -Payload $start.Payload -Names @('controlNonce')
    if ([uint32]$start.Payload.controlNonce -eq 0) {
        throw 'The acknowledged controller start did not return a control nonce.'
    }

    $readyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(
        $ReadyTimeoutSeconds)
    $mountReady = $false
    do {
        $status = Invoke-VerifiedStatus `
            -ExpectedProcessId $explorerProcessId `
            -AllowedControllerStatus @('mount-ready', 'mount-pending')
        if ($status.Payload.controllerStatus -ceq 'mount-ready') {
            $mountReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $readyDeadline)
    if (-not $mountReady) {
        throw "The native weather button did not become mount-ready in $ReadyTimeoutSeconds seconds."
    }

    if ($ReadyHoldSeconds -gt 0) {
        $holdDeadline = [DateTimeOffset]::UtcNow.AddSeconds($ReadyHoldSeconds)
        do {
            $remaining = [Math]::Ceiling(
                ($holdDeadline - [DateTimeOffset]::UtcNow).TotalSeconds)
            if ($remaining -le 0) {
                break
            }
            Start-Sleep -Seconds ([Math]::Min(5, [int]$remaining))
            Invoke-VerifiedStatus `
                -ExpectedProcessId $explorerProcessId `
                -AllowedControllerStatus @('mount-ready') | Out-Null
        } while ([DateTimeOffset]::UtcNow -lt $holdDeadline)
    }
} catch {
    $testFailure = $_.Exception.Message
} finally {
    if ($cleanupRequired) {
        try {
            Stop-ControllerWithRetry | Out-Null
            $cleanupRequired = $false
            $postStop = Invoke-TaskbarBroker -Arguments @(
                'status',
                '--host',
                $hostPath)
            Assert-ControlResult `
                -Result $postStop `
                -Command 'status' `
                -AllowedControllerStatus @('not-started')
        } catch {
            $cleanupFailure = $_.Exception.Message
        }
    }
}

if (-not [string]::IsNullOrEmpty($cleanupFailure)) {
    $message = 'Explorer controller cleanup could not be verified. '
    if (-not [string]::IsNullOrEmpty($testFailure)) {
        $message += "Test failure: $testFailure "
    }
    $message += (
        "Cleanup failure: $cleanupFailure Run the Broker stop command " +
        'manually or restart only the disposable Explorer session.')
    throw $message
}
if (-not [string]::IsNullOrEmpty($testFailure)) {
    throw $testFailure
}

Write-Host (
    'Standalone taskbar controller reached mount-ready, remained ready for ' +
    "$ReadyHoldSeconds seconds, and stopped cleanly.") -ForegroundColor Green
