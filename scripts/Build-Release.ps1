[CmdletBinding()]
param(
    [ValidatePattern('^v[0-9]{3}$')]
    [string]$Version = 'v006',
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

if ($Clean -and (Test-Path -LiteralPath $releaseRoot)) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $releaseRoot) -Recurse -Force
}
if (Test-Path -LiteralPath $stageDirectory) {
    Remove-Item -LiteralPath (Assert-RepositoryChild $stageDirectory) -Recurse -Force
}
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

dotnet build (Join-Path $repositoryRoot 'TrackSwap.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw "Managed build failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.Protocol.Tests\TrackSwap.Protocol.Tests.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Protocol tests failed with exit code $LASTEXITCODE." }
dotnet test (Join-Path $repositoryRoot 'tests\TrackSwap.Runtime.Tests\TrackSwap.Runtime.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Runtime tests failed with exit code $LASTEXITCODE." }

& (Join-Path $PSScriptRoot 'Build-Driver.ps1') `
    -Configuration Release `
    -BuildDirectory $driverBuildDirectory `
    -PackageDirectory $driverPackageDirectory `
    -Clean:$Clean

dotnet publish (Join-Path $repositoryRoot 'src\TrackSwap\TrackSwap.csproj') `
    -c Release --no-build -o $stageDirectory
if ($LASTEXITCODE -ne 0) { throw "UI publish failed with exit code $LASTEXITCODE." }
$runtimeDirectory = Join-Path $stageDirectory 'runtime'
dotnet publish (Join-Path $repositoryRoot 'src\TrackSwap.Runtime\TrackSwap.Runtime.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $runtimeDirectory
if ($LASTEXITCODE -ne 0) { throw "Runtime publish failed with exit code $LASTEXITCODE." }

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
