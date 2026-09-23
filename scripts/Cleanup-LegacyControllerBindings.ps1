[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$JsonLibraryPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OpenVrDriverTools.ps1')

Assert-SteamVrStopped

$resolvedJsonLibrary = [System.IO.Path]::GetFullPath($JsonLibraryPath)
if (-not (Test-Path -LiteralPath $resolvedJsonLibrary)) {
    throw "Newtonsoft.Json was not found at $resolvedJsonLibrary."
}
[void][System.Reflection.Assembly]::LoadFrom($resolvedJsonLibrary)

$registryPath = Join-Path $env:LOCALAPPDATA 'openvr\openvrpaths.vrpath'
if (-not (Test-Path -LiteralPath $registryPath)) {
    throw "OpenVR path registry was not found at $registryPath."
}
$registry = Get-Content -LiteralPath $registryPath -Raw | ConvertFrom-Json
$configDirectory = @($registry.config) | Where-Object { $_ } | Select-Object -First 1
if (-not $configDirectory) {
    throw "No SteamVR configuration directory was found in $registryPath."
}

$settingsPath = Join-Path ([System.IO.Path]::GetFullPath($configDirectory)) 'steamvr.vrsettings'
if (-not (Test-Path -LiteralPath $settingsPath)) {
    throw "steamvr.vrsettings was not found at $settingsPath."
}

$root = [Newtonsoft.Json.Linq.JObject]::Parse(
    [System.IO.File]::ReadAllText($settingsPath))
$removedKeys = [System.Collections.Generic.List[string]]::new()
foreach ($section in @($root.Properties())) {
    if ($section.Value -isnot [Newtonsoft.Json.Linq.JObject]) {
        continue
    }
    foreach ($property in @($section.Value.Properties())) {
        if ($property.Name -like 'trackswap_controller_*') {
            $removedKeys.Add($section.Name + '/' + $property.Name)
            $property.Remove()
        }
    }
}

if ($removedKeys.Count -eq 0) {
    Write-Host 'No legacy TrackSwap controller binding selections were found.'
    exit 0
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backupPath = $settingsPath + '.trackswap-touch-migration-' + $timestamp + '.backup'
$temporaryPath = $settingsPath + '.trackswap-touch-migration.tmp'
$rollbackPath = $settingsPath + '.trackswap-touch-migration.rollback'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::Copy($settingsPath, $backupPath, $false)
[System.IO.File]::WriteAllText(
    $temporaryPath,
    $root.ToString([Newtonsoft.Json.Formatting]::Indented) + [Environment]::NewLine,
    $utf8)

try {
    [System.IO.File]::Replace($temporaryPath, $settingsPath, $rollbackPath, $true)
}
finally {
    if ([System.IO.File]::Exists($temporaryPath)) {
        [System.IO.File]::Delete($temporaryPath)
    }
    if ([System.IO.File]::Exists($rollbackPath)) {
        [System.IO.File]::Delete($rollbackPath)
    }
}

Write-Host ('Removed legacy TrackSwap binding selections: ' +
    [string]::Join(', ', $removedKeys))
Write-Host "Backup: $backupPath"
