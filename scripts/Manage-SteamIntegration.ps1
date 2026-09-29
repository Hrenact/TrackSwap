[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$InstallRoot
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($InstallRoot)
$trackSwapExecutable = Join-Path $root 'TrackSwap.exe'
$manifestPath = Join-Path $root 'TrackSwap.vrmanifest'
$jsonLibraryPath = Join-Path $root 'Newtonsoft.Json.dll'
$scriptDirectory = Join-Path $root 'scripts'

if (-not (Test-Path -LiteralPath $trackSwapExecutable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $jsonLibraryPath -PathType Leaf)) {
    throw "TrackSwap Steam integration files are incomplete under $root."
}

if ($Mode -eq 'Install') {
    $driverRegistered = $false
    try {
        & (Join-Path $scriptDirectory 'Install-Driver.ps1') `
            -DriverPath (Join-Path $root 'driver\trackswap') `
            -ReplaceExisting
        if ($LASTEXITCODE -ne 0) {
            throw "TrackSwap driver registration failed with exit code $LASTEXITCODE."
        }
        $driverRegistered = $true

        & (Join-Path $scriptDirectory 'Manage-SteamVrRegistration.ps1') `
            -Mode Install `
            -ManifestPath $manifestPath `
            -JsonLibraryPath $jsonLibraryPath
        if ($LASTEXITCODE -ne 0) {
            throw "TrackSwap application registration failed with exit code $LASTEXITCODE."
        }
    }
    catch {
        if ($driverRegistered) {
            try {
                & (Join-Path $scriptDirectory 'Uninstall-Driver.ps1')
            }
            catch {
                Write-Warning ('Driver rollback also failed: ' + $_.Exception.Message)
            }
        }
        throw
    }
    exit 0
}

$failures = [System.Collections.Generic.List[string]]::new()
try {
    & (Join-Path $scriptDirectory 'Manage-SteamVrRegistration.ps1') `
        -Mode Uninstall `
        -ManifestPath $manifestPath `
        -JsonLibraryPath $jsonLibraryPath
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("application registration cleanup exited with $LASTEXITCODE")
    }
}
catch {
    $failures.Add($_.Exception.Message)
}

try {
    & (Join-Path $scriptDirectory 'Uninstall-Driver.ps1')
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("driver cleanup exited with $LASTEXITCODE")
    }
}
catch {
    $failures.Add($_.Exception.Message)
}

if ($failures.Count -ne 0) {
    throw ('TrackSwap Steam integration cleanup was incomplete: ' +
        ($failures -join '; '))
}

# UserData is intentionally not removed. It is runtime-created, excluded from
# the depot, and remains available if the user installs TrackSwap again.
exit 0
