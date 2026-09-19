function Get-SteamVrRuntimePath {
    $registryFile = Join-Path $env:LOCALAPPDATA "openvr\openvrpaths.vrpath"
    if (-not (Test-Path -LiteralPath $registryFile)) {
        throw "OpenVR path registry was not found at $registryFile. Start SteamVR once, then retry."
    }

    $registry = Get-Content -LiteralPath $registryFile -Raw | ConvertFrom-Json
    $runtime = @($registry.runtime) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
        Select-Object -First 1
    if (-not $runtime) {
        throw "No installed SteamVR runtime was found in $registryFile."
    }

    return [System.IO.Path]::GetFullPath($runtime)
}

function Get-VrPathReg {
    $runtime = Get-SteamVrRuntimePath
    $candidate = Join-Path $runtime "bin\win64\vrpathreg.exe"
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "SteamVR's vrpathreg.exe was not found at $candidate."
    }

    return $candidate
}

function Assert-SteamVrStopped {
    $running = @("vrserver", "vrmonitor", "vrcompositor") |
        ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue } |
        Select-Object -First 1
    if ($running) {
        throw "SteamVR must be fully stopped before registering or removing the development driver."
    }
}
