$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $env:TEMP ('TrackSwap-installer-test-' + [Guid]::NewGuid().ToString('N'))
$configFixture = Join-Path $repositoryRoot (
    'artifacts\installer-registration-test-' + [Guid]::NewGuid().ToString('N'))
$localAppData = Join-Path $fixture 'LocalAppData'
# Keep the Steam fixture on the repository volume while LOCALAPPDATA remains
# under %TEMP%. On the maintainer machine this reproduces the common C:/D: or
# C:/F: split that System.IO.File.Replace must handle.
$configDirectory = Join-Path $configFixture 'SteamConfig'
$openVrDirectory = Join-Path $localAppData 'openvr'
$manifestPath = Join-Path $fixture 'TrackSwap.vrmanifest'
$jsonLibrary = Join-Path $repositoryRoot 'src\TrackSwap\bin\Release\net48\Newtonsoft.Json.dll'
$originalLocalAppData = $env:LOCALAPPDATA

try {
    New-Item -ItemType Directory -Path `
        $openVrDirectory, `
        $configDirectory, `
        (Join-Path $configDirectory 'vrappconfig'), `
        (Join-Path $localAppData 'TrackSwap') -Force | Out-Null

    @{
        config = @($configDirectory)
        runtime = @('X:\SteamVR')
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $openVrDirectory 'openvrpaths.vrpath') `
        -Encoding UTF8
    @{
        manifest_paths = @(
            'X:\Other\Other.vrmanifest',
            'X:\Old\TrackSwap.vrmanifest')
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $configDirectory 'appconfig.json') `
        -Encoding UTF8
    @'
{
  "TrackingOverrides": {
    "/devices/trackswap/TRKSWAP-PROXY-00": "/devices/test/target",
    "/devices/other/source": "/devices/other/target"
  },
  "other": { "keep": true }
}
'@ | Set-Content `
        -LiteralPath (Join-Path $configDirectory 'steamvr.vrsettings') `
        -Encoding UTF8
    '{"applications":[{"app_key":"com.hrenact.trackswap"}]}' |
        Set-Content -LiteralPath $manifestPath -Encoding UTF8
    'keep until purge' | Set-Content `
        -LiteralPath (Join-Path $localAppData 'TrackSwap\runtime-config.json') `
        -Encoding UTF8

    $env:LOCALAPPDATA = $localAppData
    & (Join-Path $repositoryRoot 'scripts\Manage-SteamVrRegistration.ps1') `
        -Mode Install `
        -ManifestPath $manifestPath `
        -JsonLibraryPath $jsonLibrary

    $installedApp = Get-Content `
        -LiteralPath (Join-Path $configDirectory 'appconfig.json') `
        -Raw | ConvertFrom-Json
    $installedVrApp = Get-Content `
        -LiteralPath (Join-Path $configDirectory 'vrappconfig\com.hrenact.trackswap.vrappconfig') `
        -Raw | ConvertFrom-Json
    if (@($installedApp.manifest_paths).Count -ne 2 -or
        $installedApp.manifest_paths -notcontains $manifestPath -or
        $installedVrApp.autolaunch -ne $false) {
        throw ('Install registration assertions failed: app=' +
            ($installedApp | ConvertTo-Json -Depth 8 -Compress) +
            '; vrapp=' + ($installedVrApp | ConvertTo-Json -Depth 8 -Compress))
    }

    & (Join-Path $repositoryRoot 'scripts\Manage-SteamVrRegistration.ps1') `
        -Mode Uninstall `
        -ManifestPath $manifestPath `
        -JsonLibraryPath $jsonLibrary `
        -PurgeUserData

    $uninstalledApp = Get-Content `
        -LiteralPath (Join-Path $configDirectory 'appconfig.json') `
        -Raw | ConvertFrom-Json
    $uninstalledSettings = Get-Content `
        -LiteralPath (Join-Path $configDirectory 'steamvr.vrsettings') `
        -Raw | ConvertFrom-Json
    if (@($uninstalledApp.manifest_paths).Count -ne 1 -or
        $uninstalledApp.manifest_paths[0] -ne 'X:\Other\Other.vrmanifest') {
        throw 'Application manifest cleanup assertions failed.'
    }
    if ($uninstalledSettings.TrackingOverrides.PSObject.Properties.Name -contains
            '/devices/trackswap/TRKSWAP-PROXY-00' -or
        $uninstalledSettings.TrackingOverrides.'/devices/other/source' -ne
            '/devices/other/target' -or
        -not $uninstalledSettings.other.keep) {
        throw ('Tracking override cleanup assertions failed: ' +
            ($uninstalledSettings | ConvertTo-Json -Depth 8 -Compress))
    }
    if (Test-Path -LiteralPath (
        Join-Path $configDirectory 'vrappconfig\com.hrenact.trackswap.vrappconfig')) {
        throw 'TrackSwap vrappconfig remained after uninstall.'
    }
    if (Test-Path -LiteralPath (Join-Path $localAppData 'TrackSwap')) {
        throw 'TrackSwap user data remained after uninstall.'
    }

    Write-Host 'Installer registration tests passed.'
}
finally {
    $env:LOCALAPPDATA = $originalLocalAppData
    $resolvedFixture = [System.IO.Path]::GetFullPath($fixture)
    $temporaryRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
    if (-not $resolvedFixture.StartsWith(
        $temporaryRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean installer fixture outside TEMP: $resolvedFixture"
    }
    if (Test-Path -LiteralPath $resolvedFixture) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }

    $resolvedConfigFixture = [System.IO.Path]::GetFullPath($configFixture)
    $artifactRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot 'artifacts')).TrimEnd('\') + '\'
    if (-not $resolvedConfigFixture.StartsWith(
        $artifactRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean installer fixture outside artifacts: $resolvedConfigFixture"
    }
    if (Test-Path -LiteralPath $resolvedConfigFixture) {
        Remove-Item -LiteralPath $resolvedConfigFixture -Recurse -Force
    }
}
