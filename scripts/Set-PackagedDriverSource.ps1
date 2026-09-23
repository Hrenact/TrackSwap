[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^/devices/.+')]
    [string]$SourceDevicePath,
    [string]$DriverPath
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "OpenVrDriverTools.ps1")

Assert-SteamVrStopped

if (-not $DriverPath) {
    $DriverPath = Join-Path $PSScriptRoot "..\artifacts\driver\trackswap"
}

$resolvedDriverPath = [System.IO.Path]::GetFullPath($DriverPath)
$settingsPath = Join-Path $resolvedDriverPath "resources\settings\default.vrsettings"
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "Packaged driver settings were not found at $settingsPath. Run scripts\Build-Driver.ps1 first."
}

if ($SourceDevicePath -imatch '^/devices/trackswap/TRKSWAP-(PROXY-[0-9]{2}|TRACKER-[0-9]{2}|CONTROLLER-[LR])$') {
    throw "The TrackSwap virtual device cannot be its own source."
}

$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.driver_trackswap.sourceDevicePath = $SourceDevicePath
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsPath -Encoding utf8

Write-Host "Configured packaged driver source: $SourceDevicePath"
Write-Host "This machine-local value is under artifacts and will not be committed."
