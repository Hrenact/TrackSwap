[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$JsonLibraryPath,

    [switch]$PurgeUserData
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OpenVrDriverTools.ps1')

Assert-SteamVrStopped

$resolvedJsonLibrary = [System.IO.Path]::GetFullPath($JsonLibraryPath)
if (-not (Test-Path -LiteralPath $resolvedJsonLibrary)) {
    throw "Newtonsoft.Json was not found at $resolvedJsonLibrary."
}
[void][System.Reflection.Assembly]::LoadFrom($resolvedJsonLibrary)

function Get-SteamVrConfigDirectory {
    $registryFile = Join-Path $env:LOCALAPPDATA 'openvr\openvrpaths.vrpath'
    if (-not (Test-Path -LiteralPath $registryFile)) {
        throw "OpenVR path registry was not found at $registryFile. Start SteamVR once, then retry."
    }

    $registry = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json
    $configDirectory = @($registry.config) |
        Where-Object { $_ } |
        Select-Object -First 1
    if (-not $configDirectory) {
        throw "No SteamVR configuration directory was found in $registryFile."
    }

    return [System.IO.Path]::GetFullPath($configDirectory)
}

function Read-JsonObject([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Write-Output -NoEnumerate ([Newtonsoft.Json.Linq.JObject]::new())
        return
    }
    Write-Output -NoEnumerate (
        [Newtonsoft.Json.Linq.JObject]::Parse([System.IO.File]::ReadAllText($Path)))
}

function Write-JsonObjectAtomic(
    [string]$Path,
    [Newtonsoft.Json.Linq.JObject]$Root) {
    $directory = [System.IO.Path]::GetDirectoryName($Path)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = $Path + '.trackswap.tmp'
    # File.Replace requires the target, replacement, and backup to use the
    # same volume. Steam is commonly installed on a different drive from
    # %TEMP%, so keep the short-lived rollback copy beside the target file.
    $backupPath = $Path + '.trackswap-' + [Guid]::NewGuid().ToString('N') + '.backup'
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        $temporaryPath,
        $Root.ToString([Newtonsoft.Json.Formatting]::Indented) + [Environment]::NewLine,
        $utf8)

    try {
        if (Test-Path -LiteralPath $Path) {
            [System.IO.File]::Replace($temporaryPath, $Path, $backupPath, $true)
        }
        else {
            [System.IO.File]::Move($temporaryPath, $Path)
        }
    }
    catch {
        if (Test-Path -LiteralPath $backupPath) {
            Copy-Item -LiteralPath $backupPath -Destination $Path -Force
        }
        throw
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-TrackSwapManifestPath([string]$Candidate, [string]$Expected) {
    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $false
    }

    try {
        if ([System.IO.Path]::GetFullPath($Candidate) -ieq $Expected) {
            return $true
        }
    }
    catch {
    }

    if ([System.IO.Path]::GetFileName($Candidate) -ieq 'TrackSwap.vrmanifest') {
        return $true
    }

    if (Test-Path -LiteralPath $Candidate) {
        try {
            return [System.IO.File]::ReadAllText($Candidate).Contains('com.hrenact.trackswap')
        }
        catch {
        }
    }
    return $false
}

$configDirectory = Get-SteamVrConfigDirectory
$resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
$appConfigPath = Join-Path $configDirectory 'appconfig.json'
$appConfig = Read-JsonObject $appConfigPath
$manifestProperty = $appConfig.Property('manifest_paths')
[Newtonsoft.Json.Linq.JArray]$manifestPaths = $null
if ($null -ne $manifestProperty) {
    $manifestPaths = $manifestProperty.Value
}
if ($null -eq $manifestPaths) {
    $appConfig.Merge([Newtonsoft.Json.Linq.JObject]::Parse('{"manifest_paths":[]}'))
    $manifestPaths = $appConfig.Property('manifest_paths').Value -as [Newtonsoft.Json.Linq.JArray]
}

$manifestIndex = $manifestPaths.Count - 1
while ($manifestIndex -ge 0) {
    $entry = $manifestPaths[$manifestIndex]
    $candidatePath = if ($entry -is [Newtonsoft.Json.Linq.JValue]) {
        [string]$entry.Value
    }
    else {
        [string]$entry
    }
    if (Test-TrackSwapManifestPath $candidatePath $resolvedManifest) {
        $manifestPaths.RemoveAt($manifestIndex)
    }
    $manifestIndex--
}
if ($Mode -eq 'Install') {
    if (-not (Test-Path -LiteralPath $resolvedManifest)) {
        throw "TrackSwap.vrmanifest was not found at $resolvedManifest."
    }
    $manifestPaths.Add([Newtonsoft.Json.Linq.JValue]::new($resolvedManifest))
}
Write-JsonObjectAtomic $appConfigPath $appConfig

$vrAppConfigDirectory = Join-Path $configDirectory 'vrappconfig'
$vrAppConfigPath = Join-Path $vrAppConfigDirectory 'com.hrenact.trackswap.vrappconfig'
if ($Mode -eq 'Install') {
    if (-not (Test-Path -LiteralPath $vrAppConfigPath)) {
        $vrAppConfig = [Newtonsoft.Json.Linq.JObject]::Parse(
            '{"autolaunch":false,"last_launch_time":"0"}')
        Write-JsonObjectAtomic $vrAppConfigPath $vrAppConfig
    }
}
else {
    Remove-Item -LiteralPath $vrAppConfigPath -Force -ErrorAction SilentlyContinue

    $settingsPath = Join-Path $configDirectory 'steamvr.vrsettings'
    if (Test-Path -LiteralPath $settingsPath) {
        $settings = Read-JsonObject $settingsPath
        $overridesProperty = $settings.Property('TrackingOverrides')
        [Newtonsoft.Json.Linq.JObject]$overrides = $null
        if ($null -ne $overridesProperty) {
            $overrides = $overridesProperty.Value
        }
        $settingsChanged = $false
        if ($null -ne $overrides) {
            $removeProperty = [Newtonsoft.Json.Linq.JObject].GetMethod(
                'Remove',
                [Type[]]@([string]))
            for ($slot = 0; $slot -lt 8; $slot++) {
                $sourcePath = '/devices/trackswap/TRKSWAP-PROXY-' + $slot.ToString('00')
                $removed = [bool]$removeProperty.Invoke(
                    $overrides,
                    [object[]]@($sourcePath))
                if ($removed) {
                    $settingsChanged = $true
                }
            }
        }
        if ($settingsChanged) {
            Write-JsonObjectAtomic $settingsPath $settings
        }
    }

    Get-ChildItem -LiteralPath $configDirectory -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like 'steamvr.vrsettings.trackswap-*.backup' -or
            $_.Name -eq 'steamvr.vrsettings.trackswap.tmp' -or
            $_.Name -like 'appconfig.json.trackswap-*.backup' -or
            $_.Name -eq 'appconfig.json.trackswap.tmp'
        } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    if ($PurgeUserData) {
        $userDataDirectory = Join-Path $env:LOCALAPPDATA 'TrackSwap'
        if (Test-Path -LiteralPath $userDataDirectory) {
            Remove-Item -LiteralPath $userDataDirectory -Recurse -Force
        }
    }
}

Write-Host "$Mode SteamVR application registration completed."
