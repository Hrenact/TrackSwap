[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$BuildDirectory,
    [string]$PackageDirectory,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourceDirectory = Join-Path $repositoryRoot "src\TrackSwap.Driver"
if (-not $BuildDirectory) {
    $BuildDirectory = Join-Path $repositoryRoot "artifacts\build\driver"
}
if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repositoryRoot "artifacts\driver\trackswap"
}
$buildDirectory = [System.IO.Path]::GetFullPath($BuildDirectory)
$packageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)

function Find-CMake {
    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $installations = & $vswhere -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        foreach ($installation in $installations) {
            $candidate = Join-Path $installation "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    throw "CMake and the Visual Studio C++ x64 toolchain are required. Install the Desktop development with C++ workload."
}

$cmake = Find-CMake

if ($Clean) {
    foreach ($target in @($buildDirectory, $packageDirectory)) {
        $resolvedRoot = [System.IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\'
        $resolvedTarget = [System.IO.Path]::GetFullPath($target)
        if (-not $resolvedTarget.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean a path outside the repository: $resolvedTarget"
        }

        if (Test-Path -LiteralPath $resolvedTarget) {
            Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
        }
    }
}

& $cmake -S $sourceDirectory -B $buildDirectory -A x64
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed with exit code $LASTEXITCODE." }

& $cmake --build $buildDirectory --config $Configuration
if ($LASTEXITCODE -ne 0) { throw "Driver build failed with exit code $LASTEXITCODE." }

$ctest = Join-Path (Split-Path -Parent $cmake) "ctest.exe"
& $ctest --test-dir $buildDirectory -C $Configuration --output-on-failure
if ($LASTEXITCODE -ne 0) { throw "Driver tests failed with exit code $LASTEXITCODE." }

& $cmake --install $buildDirectory --config $Configuration --prefix $packageDirectory
if ($LASTEXITCODE -ne 0) { throw "Driver packaging failed with exit code $LASTEXITCODE." }

Write-Host "Driver package: $packageDirectory"
