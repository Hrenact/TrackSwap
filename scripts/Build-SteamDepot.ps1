[CmdletBinding()]
param(
    [ValidatePattern('^v[0-9]{3}$')]
    [string]$Version = 'v010',

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [uint32]::MaxValue)]
    [uint32]$AppId,

    [Parameter(Mandatory = $true)]
    [ValidateRange(1, [uint32]::MaxValue)]
    [uint32]$DepotId,

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseStage = Join-Path $repositoryRoot "artifacts\release\$Version\TrackSwap-$Version-win-x64"
$steamRoot = Join-Path $repositoryRoot "artifacts\steam\$Version"
$contentRoot = Join-Path $steamRoot 'content'
$configRoot = Join-Path $steamRoot 'scripts'
$outputRoot = Join-Path $steamRoot 'output'

function Assert-RepositoryChild([string]$Path) {
    $rootPrefix = $repositoryRoot.TrimEnd('\') + '\'
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $resolved"
    }
    return $resolved
}

if ($Clean -and (Test-Path -LiteralPath $steamRoot)) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $steamRoot) -Recurse -Force
}

& (Join-Path $PSScriptRoot 'Build-Release.ps1') -Version $Version -Clean:$Clean
if ($LASTEXITCODE -ne 0) {
    throw "TrackSwap release build failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $contentRoot) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $contentRoot) -Recurse -Force
}
New-Item -ItemType Directory -Path $contentRoot, $configRoot, $outputRoot -Force | Out-Null
Copy-Item -Path (Join-Path $releaseStage '*') -Destination $contentRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Manage-SteamIntegration.ps1') `
    -Destination (Join-Path $contentRoot 'scripts') -Force

$unexpectedFiles = Get-ChildItem -LiteralPath $contentRoot -Recurse -Force |
    Where-Object {
        $_.FullName -match '\\UserData(?:\\|$)' -or
        $_.Extension -eq '.pdb'
    }
if ($unexpectedFiles) {
    throw ('Steam depot staging contains excluded runtime/developer files: ' +
        (($unexpectedFiles | ForEach-Object FullName) -join '; '))
}

$installScriptPath = Join-Path $contentRoot 'installscript.vdf'
$installScript = @"
"InstallScript"
{
    "Run Process"
    {
        "TrackSwap OpenVR integration"
        {
            "Process 1" "%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"
            "Command 1" "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%INSTALLDIR%\\scripts\\Manage-SteamIntegration.ps1\" -Mode Install -InstallRoot \"%INSTALLDIR%\""
        }
    }
    "Run Process On Uninstall"
    {
        "TrackSwap OpenVR integration"
        {
            "Process 1" "%WINDIR%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe"
            "Command 1" "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"%INSTALLDIR%\\scripts\\Manage-SteamIntegration.ps1\" -Mode Uninstall -InstallRoot \"%INSTALLDIR%\""
        }
    }
}
"@
Set-Content -LiteralPath $installScriptPath -Value $installScript -Encoding UTF8

$depotScriptPath = Join-Path $configRoot "depot_build_$DepotId.vdf"
$depotScript = @"
"DepotBuild"
{
    "DepotID" "$DepotId"
    "ContentRoot" "..\\content"
    "FileMapping"
    {
        "LocalPath" "*"
        "DepotPath" "."
        "Recursive" "1"
    }
    "FileExclusion" "UserData\\*"
    "FileExclusion" "*.pdb"
    "InstallScript" "installscript.vdf"
}
"@
Set-Content -LiteralPath $depotScriptPath -Value $depotScript -Encoding UTF8

$appScriptPath = Join-Path $configRoot "app_build_$AppId.vdf"
$appScript = @"
"AppBuild"
{
    "AppID" "$AppId"
    "Desc" "TrackSwap $Version"
    "Preview" "1"
    "BuildOutput" "..\\output"
    "Depots"
    {
        "$DepotId" "depot_build_$DepotId.vdf"
    }
}
"@
Set-Content -LiteralPath $appScriptPath -Value $appScript -Encoding UTF8

Write-Host "Steam depot content: $contentRoot"
Write-Host "SteamPipe preview config: $appScriptPath"
Write-Host 'Preview is enabled. Remove "Preview" "1" only when the manifest has been inspected and the assigned AppID/DepotID are correct.'
