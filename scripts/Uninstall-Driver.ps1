[CmdletBinding()]
param(
    [string]$DriverPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "OpenVrDriverTools.ps1")

Assert-SteamVrStopped

$vrpathreg = Get-VrPathReg
if (-not $DriverPath) {
    $registeredPath = & $vrpathreg finddriver trackswap 2>$null
    $findExitCode = $LASTEXITCODE
    if ($findExitCode -eq 1) {
        Write-Host "TrackSwap driver is not registered."
        exit 0
    }
    if ($findExitCode -ne 0) {
        throw "Expected exactly one registered TrackSwap driver; vrpathreg returned $findExitCode."
    }

    $DriverPath = ($registeredPath | Select-Object -Last 1).Trim()
}

$resolvedDriverPath = [System.IO.Path]::GetFullPath($DriverPath)
& $vrpathreg removedriver $resolvedDriverPath
if ($LASTEXITCODE -ne 0) { throw "vrpathreg removedriver failed with exit code $LASTEXITCODE." }

Write-Host "Unregistered TrackSwap driver at $resolvedDriverPath"
