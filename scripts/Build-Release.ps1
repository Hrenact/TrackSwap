[CmdletBinding()]
param(
    [ValidatePattern('^v[0-9]{3}$')]
    [string]$Version = 'v012',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot "artifacts\release\$Version"
$stageDirectory = Join-Path $releaseRoot "TrackSwap-$Version-win-x64"
$driverBuildDirectory = Join-Path $repositoryRoot "artifacts\build\driver-$Version-release"
$driverPackageDirectory = Join-Path $repositoryRoot "artifacts\build\driver-$Version-package\trackswap"

function Assert-RepositoryChild([string]$Path) {
    $rootPrefix = $repositoryRoot.TrimEnd('\') + '\'
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the repository: $resolved"
    }
    return $resolved
}

& (Join-Path $PSScriptRoot 'Test-ThirdPartyNotices.ps1')

if ($Clean -and (Test-Path -LiteralPath $releaseRoot)) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $releaseRoot) -Recurse -Force
}
if (Test-Path -LiteralPath $stageDirectory) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $stageDirectory) -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

dotnet build (Join-Path $repositoryRoot 'TrackSwap.sln') -c Release `
    -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Managed build failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.Protocol.Tests\TrackSwap.Protocol.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Protocol tests failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.Tests\TrackSwap.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "UI storage tests failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.Runtime.Tests\TrackSwap.Runtime.Tests.csproj') `
    -c Release -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "Runtime tests failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.HapticPhoneDiagnostic.Tests\TrackSwap.HapticPhoneDiagnostic.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Phone haptic diagnostic tests failed with exit code $LASTEXITCODE." }
& powershell -NoProfile -ExecutionPolicy Bypass -File `
    (Join-Path $repositoryRoot 'tests\InstallerRegistration.Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw "Installer registration tests failed with exit code $LASTEXITCODE." }
& (Join-Path $repositoryRoot 'tests\LocalizationCrowdin.Tests.ps1')

& (Join-Path $PSScriptRoot 'Build-Driver.ps1') `
    -Configuration Release `
    -BuildDirectory $driverBuildDirectory `
    -PackageDirectory $driverPackageDirectory `
    -Clean:$Clean

dotnet publish (Join-Path $repositoryRoot 'src\TrackSwap\TrackSwap.csproj') `
    -c Release --no-build -p:DebugType=None -p:DebugSymbols=false -o $stageDirectory
if ($LASTEXITCODE -ne 0) { throw "UI publish failed with exit code $LASTEXITCODE." }
$runtimeDirectory = Join-Path $stageDirectory 'runtime'
dotnet publish (Join-Path $repositoryRoot 'src\TrackSwap.Runtime\TrackSwap.Runtime.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false `
    -p:DebugType=None -p:DebugSymbols=false -o $runtimeDirectory
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed with exit code $LASTEXITCODE." }

$runtimeConfigPath = Join-Path $runtimeDirectory 'TrackSwap.Runtime.runtimeconfig.json'
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$runtimeFramework = @($runtimeConfig.runtimeOptions.includedFrameworks) |
    Where-Object { $_.name -eq 'Microsoft.NETCore.App' } |
    Select-Object -First 1
if ($null -eq $runtimeFramework -or [string]::IsNullOrWhiteSpace([string]$runtimeFramework.version)) {
    throw "Cannot determine the resolved Microsoft.NETCore.App version from $runtimeConfigPath."
}
$runtimeVersion = [string]$runtimeFramework.version
$assetsPath = Join-Path $repositoryRoot 'src\TrackSwap.Runtime\obj\project.assets.json'
$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$packageRoots = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $packageRoots.Add($env:NUGET_PACKAGES)
}
foreach ($property in $assets.packageFolders.PSObject.Properties) {
    $packageRoots.Add($property.Name.TrimEnd('\', '/'))
}
$packageRoots.Add((Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'))
$runtimePackDirectory = $null
foreach ($packageRoot in $packageRoots | Select-Object -Unique) {
    $candidate = Join-Path $packageRoot "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        $runtimePackDirectory = $candidate
        break
    }
}
if ($null -eq $runtimePackDirectory) {
    throw "Cannot find Microsoft.NETCore.App.Runtime.win-x64 $runtimeVersion in the restored NuGet package folders."
}
Copy-Item -LiteralPath (Join-Path $runtimePackDirectory 'LICENSE.TXT') `
    -Destination (Join-Path $runtimeDirectory 'DOTNET-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $runtimePackDirectory 'THIRD-PARTY-NOTICES.TXT') `
    -Destination (Join-Path $runtimeDirectory 'DOTNET-THIRD-PARTY-NOTICES.txt') -Force
& (Join-Path $PSScriptRoot 'Test-ThirdPartyNotices.ps1') -RuntimeDirectory $runtimeDirectory

# Symbols are useful in CI artifacts, but the end-user ZIP and Steam depot must
# not ship developer PDBs or expose source-path metadata.
Get-ChildItem -LiteralPath $stageDirectory -Filter '*.pdb' -Recurse -File |
    Remove-Item -Force

New-Item -ItemType Directory -Path (Join-Path $stageDirectory 'driver') -Force | Out-Null
Copy-Item -LiteralPath $driverPackageDirectory -Destination (Join-Path $stageDirectory 'driver\trackswap') -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $stageDirectory 'scripts') -Force | Out-Null
foreach ($scriptName in @(
    'Install-Driver.ps1',
    'Uninstall-Driver.ps1',
    'OpenVrDriverTools.ps1',
    'Manage-SteamVrRegistration.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $stageDirectory 'scripts') -Force
}
foreach ($fileName in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $fileName) -Destination $stageDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs') -Destination $stageDirectory -Recurse -Force

$zipPath = Join-Path $releaseRoot "TrackSwap-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $stageDirectory -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$zipPath.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $([System.IO.Path]::GetFileName($zipPath))" -Encoding ascii

Write-Host "Release package: $zipPath"
Write-Host "SHA-256: $hash"
