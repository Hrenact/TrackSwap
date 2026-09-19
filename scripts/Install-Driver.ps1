[CmdletBinding()]
param(
    [string]$DriverPath,
    [switch]$ReplaceExisting
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "OpenVrDriverTools.ps1")

Assert-SteamVrStopped

if (-not $DriverPath) {
    $packagedPath = Join-Path $PSScriptRoot "..\driver\trackswap"
    $developmentPath = Join-Path $PSScriptRoot "..\artifacts\driver\trackswap"
    $DriverPath = if (Test-Path -LiteralPath $packagedPath) { $packagedPath } else { $developmentPath }
}

$resolvedDriverPath = [System.IO.Path]::GetFullPath($DriverPath)
$manifest = Join-Path $resolvedDriverPath "driver.vrdrivermanifest"
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "The packaged driver manifest was not found at $manifest. Run scripts\Build-Driver.ps1 first."
}

$vrpathreg = Get-VrPathReg
$registeredPath = & $vrpathreg finddriver trackswap 2>$null
$findExitCode = $LASTEXITCODE
if ($findExitCode -eq 0) {
    $existing = [System.IO.Path]::GetFullPath(($registeredPath | Select-Object -Last 1).Trim())
    if ($existing -ieq $resolvedDriverPath) {
        Write-Host "TrackSwap driver is already registered at $resolvedDriverPath"
        exit 0
    }

    if (-not $ReplaceExisting) {
        throw "A TrackSwap driver is already registered at $existing. Re-run with -ReplaceExisting to upgrade to $resolvedDriverPath."
    }

    & $vrpathreg removedriver $existing
    if ($LASTEXITCODE -ne 0) { throw "Could not unregister the existing TrackSwap driver at $existing." }

    try {
        & $vrpathreg adddriver $resolvedDriverPath
        if ($LASTEXITCODE -ne 0) { throw "vrpathreg adddriver failed with exit code $LASTEXITCODE." }
    }
    catch {
        Write-Warning "Upgrade registration failed; attempting to restore $existing"
        & $vrpathreg adddriver $existing
        throw
    }

    Write-Host "Replaced TrackSwap driver registration: $existing -> $resolvedDriverPath"
    exit 0
}
if ($findExitCode -eq 2) {
    throw "Multiple TrackSwap drivers are registered. Resolve them with vrpathreg before continuing."
}
if ($findExitCode -ne 1) {
    throw "vrpathreg finddriver failed with exit code $findExitCode."
}

& $vrpathreg adddriver $resolvedDriverPath
if ($LASTEXITCODE -ne 0) { throw "vrpathreg adddriver failed with exit code $LASTEXITCODE." }

Write-Host "Registered TrackSwap development driver at $resolvedDriverPath"
