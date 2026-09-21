param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$iconDirectory = Join-Path $RepositoryRoot 'src\TrackSwap.Driver\resources\icons'
$appIconPath = Join-Path $RepositoryRoot 'src\TrackSwap\Assets\TrackSwap.svg'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null

$edgeCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
)
$edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edgePath) {
    throw 'Microsoft Edge is required to render the SteamVR status icons.'
}

$states = [ordered]@{
    'trackswap_device_v3_off.png' = @('#777B80', '#777B80')
    'trackswap_device_v3_searching.png' = @('#5B9CFF', '#F0B43C')
    'trackswap_device_v3_searching_alert.png' = @('#F0B43C', '#F0B43C')
    'trackswap_device_v3_ready.png' = @('#5B9CFF', '#4ED18B')
    'trackswap_device_v3_ready_alert.png' = @('#F0B43C', '#F0B43C')
    'trackswap_device_v3_not_ready.png' = @('#A6A8AB', '#F0B43C')
    'trackswap_device_v3_standby.png' = @('#6F8197', '#6F8197')
    'trackswap_device_v3_alert_low.png' = @('#E26363', '#F0B43C')
    'trackswap_device_v3_standby_alert.png' = @('#F0B43C', '#E26363')
}

foreach ($entry in $states.GetEnumerator()) {
    [xml]$svg = Get-Content -LiteralPath $appIconPath -Raw
    $background = $svg.svg.rect
    if ($background) {
        [void]$svg.svg.RemoveChild($background)
    }
    $svg.svg.path.SetAttribute('fill', 'none')
    $svg.svg.path.SetAttribute('stroke', $entry.Value[0])
    $svg.svg.circle.SetAttribute('fill', $entry.Value[1])

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($entry.Key)
    $temporarySvg = Join-Path $iconDirectory "$stem.source.svg"
    $masterPng = Join-Path $iconDirectory "$stem.master.png"
    $outputPng = Join-Path $iconDirectory $entry.Key
    $svg.Save($temporarySvg)

    $svgUri = [System.Uri]::new($temporarySvg).AbsoluteUri
    & $edgePath `
        --headless `
        --disable-gpu `
        --hide-scrollbars `
        --default-background-color=00000000 `
        --force-device-scale-factor=1 `
        --window-size=512,512 `
        --screenshot=$masterPng `
        $svgUri | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $masterPng)) {
        throw "Failed to render $($entry.Key)."
    }

    $python = @'
from pathlib import Path
import sys
from PIL import Image

master_path = Path(sys.argv[1])
output_path = Path(sys.argv[2])
Image.open(master_path).convert("RGBA").resize(
    (32, 32), Image.Resampling.LANCZOS
).save(output_path, optimize=True)
'@
    $python | py - $masterPng $outputPng
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to resize $($entry.Key)."
    }

    Remove-Item -LiteralPath $temporarySvg, $masterPng -Force
}

Write-Host "Generated SteamVR driver status icons in $iconDirectory"
