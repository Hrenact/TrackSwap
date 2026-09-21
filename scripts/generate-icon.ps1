param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$assetDirectory = Join-Path $RepositoryRoot 'src\TrackSwap\Assets'
$svgPath = Join-Path $assetDirectory 'TrackSwap.svg'
$masterPngPath = Join-Path $assetDirectory 'TrackSwap-1024.png'
$edgeCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
)
$edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edgePath) {
    throw 'Microsoft Edge is required to render the SVG icon master.'
}

$svgUri = [System.Uri]::new($svgPath).AbsoluteUri
& $edgePath `
    --headless `
    --disable-gpu `
    --hide-scrollbars `
    --default-background-color=00000000 `
    --force-device-scale-factor=1 `
    --window-size=1024,1024 `
    --screenshot=$masterPngPath `
    $svgUri | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $masterPngPath)) {
    throw 'Failed to render the TrackSwap SVG icon.'
}

$python = @'
from pathlib import Path
import sys
from PIL import Image

assets = Path(sys.argv[1])
master_path = assets / "TrackSwap-1024.png"
master = Image.open(master_path).convert("RGBA")
sizes = (16, 24, 32, 48, 64, 128, 256)
frames = []
for size in sizes:
    frame = master.resize((size, size), Image.Resampling.LANCZOS)
    frame.save(assets / f"TrackSwap-{size}.png", optimize=True)
    frames.append(frame)

frames[-1].save(
    assets / "TrackSwap.ico",
    format="ICO",
    sizes=[(size, size) for size in sizes],
    append_images=frames[:-1],
)
'@

$python | py - $assetDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to generate PNG and ICO icon assets.'
}

Write-Host "Generated TrackSwap icon assets in $assetDirectory"
