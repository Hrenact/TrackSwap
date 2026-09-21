[CmdletBinding()]
param(
    [ValidatePattern('^v[0-9]{3}$')]
    [string]$Version = 'v005',
    [switch]$Clean,
    [switch]$SkipReleaseBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot "artifacts\release\$Version"
$sourceDirectory = Join-Path $releaseRoot "TrackSwap-$Version-win-x64"
$installerScript = Join-Path $repositoryRoot 'installer\TrackSwap.iss'

if (-not $SkipReleaseBuild) {
    & (Join-Path $PSScriptRoot 'Build-Release.ps1') -Version $Version -Clean:$Clean
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'TrackSwap.exe'))) {
    throw "Complete release directory was not found at $sourceDirectory."
}

$compilerCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
$compiler = $compilerCandidates | Select-Object -First 1
if (-not $compiler) {
    throw 'Inno Setup 6 was not found. Install JRSoftware.InnoSetup with winget, then retry.'
}

& $compiler `
    "/DTrackSwapVersion=$Version" `
    "/DSourceDir=$sourceDirectory" `
    "/DOutputDir=$releaseRoot" `
    $installerScript
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $releaseRoot "TrackSwap-$Version-setup-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer output was not found at $installerPath."
}
$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$installerPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $([System.IO.Path]::GetFileName($installerPath))" -Encoding ascii

Write-Host "Installer: $installerPath"
Write-Host "SHA-256: $hash"
