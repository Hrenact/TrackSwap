param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$iconDirectory = Join-Path $RepositoryRoot 'src\TrackSwap.Driver\resources\icons'
$appIconPath = Join-Path $RepositoryRoot 'src\TrackSwap\Assets\TrackSwap.svg'
$controllerIconPath = Join-Path $RepositoryRoot 'src\TrackSwap.Driver\assets\trackswap_controller.png'
New-Item -ItemType Directory -Path $iconDirectory -Force | Out-Null

$edgeCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
)
$edgePath = $edgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edgePath) {
    throw 'Microsoft Edge is required to render the SteamVR status icons.'
}
$edgeProfileDirectory = Join-Path $RepositoryRoot 'artifacts\temp\driver-icon-edge-profile'
New-Item -ItemType Directory -Path $edgeProfileDirectory -Force | Out-Null

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

function Export-SteamVrStatusIcon {
    param(
        [xml]$Svg,
        [string]$FileName
    )

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $temporarySvg = Join-Path $iconDirectory "$stem.source.svg"
    $masterPng = Join-Path $iconDirectory "$stem.master.png"
    $outputPng = Join-Path $iconDirectory $FileName
    $Svg.Save($temporarySvg)

    $svgUri = [System.Uri]::new($temporarySvg).AbsoluteUri
    $edgeArguments = @(
        '--headless=new'
        '--disable-gpu'
        '--hide-scrollbars'
        '--no-first-run'
        "--user-data-dir=`"$edgeProfileDirectory`""
        '--default-background-color=00000000'
        '--force-device-scale-factor=1'
        '--window-size=512,512'
        "--screenshot=`"$masterPng`""
        $svgUri
    )
    $edgeProcess = Start-Process `
        -FilePath $edgePath `
        -ArgumentList $edgeArguments `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($edgeProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $masterPng)) {
        throw "Failed to render $FileName."
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
        throw "Failed to resize $FileName."
    }

    Remove-Item -LiteralPath $temporarySvg, $masterPng -Force
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
    Export-SteamVrStatusIcon -Svg $svg -FileName $entry.Key
}

$controllerStates = @($states.GetEnumerator() | ForEach-Object {
    @{
        name = [System.IO.Path]::GetFileNameWithoutExtension($_.Key).Replace('trackswap_device_v3_', '')
        outline = $_.Value[0]
        indicator = $_.Value[1]
    }
}) | ConvertTo-Json -Compress

$controllerPython = @'
from pathlib import Path
import json
import sys
from PIL import Image

source_path = Path(sys.argv[1])
output_directory = Path(sys.argv[2])
states = json.loads(sys.argv[3])
source = Image.open(source_path).convert("RGBA")

def parse_hex(value):
    value = value.lstrip("#")
    return tuple(int(value[index:index + 2], 16) for index in (0, 2, 4))

source_pixels = list(source.getdata())
pixel_roles = []
for red, green, blue, alpha in source_pixels:
    if alpha == 0:
        pixel_roles.append("transparent")
    elif green > red + 40 and green > blue + 20:
        pixel_roles.append("indicator")
    elif min(red, green, blue) > 200 and max(red, green, blue) - min(red, green, blue) < 24:
        pixel_roles.append("detail")
    else:
        pixel_roles.append("outline")

for hand in ("right", "left"):
    for state in states:
        outline = parse_hex(state["outline"])
        indicator = parse_hex(state["indicator"])
        pixels = []
        for role, (_, _, _, alpha) in zip(pixel_roles, source_pixels):
            if role == "transparent":
                pixels.append((0, 0, 0, 0))
            elif role == "indicator":
                pixels.append((*indicator, alpha))
            elif role == "detail":
                pixels.append((255, 255, 255, alpha))
            else:
                pixels.append((*outline, alpha))

        rendered = Image.new("RGBA", source.size)
        rendered.putdata(pixels)
        if hand == "left":
            rendered = rendered.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        rendered = rendered.convert("RGBa").resize((32, 32), Image.Resampling.LANCZOS).convert("RGBA")
        rendered.save(
            output_directory / f"trackswap_controller_v2_{hand}_{state['name']}.png",
            optimize=True,
        )
'@

$controllerPython | py - $controllerIconPath $iconDirectory $controllerStates
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to generate virtual-controller status icons.'
}

Write-Host "Generated SteamVR driver status icons in $iconDirectory"
