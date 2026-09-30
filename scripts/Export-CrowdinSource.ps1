param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $RepositoryRoot 'localization\crowdin\source.csv'
}

$catalogPath = Join-Path $RepositoryRoot 'src\TrackSwap\Localization\zh-CN.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
if ($null -eq $catalog.strings -or $catalog.strings.Count -eq 0) {
    throw 'The Simplified Chinese localization catalog does not contain any strings.'
}

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src\TrackSwap') -Recurse -File -Include *.cs,*.xaml,*.cpp |
    Where-Object {
        $_.FullName -notmatch '\\(?:bin|obj)\\' -and
        $_.FullName -ne (Resolve-Path -LiteralPath $catalogPath).Path
    }

$sourceTexts = @{}
foreach ($file in $sourceFiles) {
    $sourceTexts[$file.FullName] = Get-Content -LiteralPath $file.FullName -Raw
}

$areaDescriptions = @{
    'app' = 'Application shell, global workflow, and window-level messages.'
    'backup' = 'SteamVR backup and restore window.'
    'cleanup' = 'Destructive cleanup workflow.'
    'common' = 'Shared action, value, or status text.'
    'device' = 'Tracked-device discovery and status.'
    'diagnostics' = 'Runtime diagnostics and support export.'
    'dialog' = 'Shared application dialog controls.'
    'language' = 'Language selection and translation diagnostics.'
    'nav' = 'Main navigation.'
    'navigation' = 'Main navigation.'
    'preview' = 'Real-time pose or telemetry preview.'
    'route' = 'Route editing, validation, and lifecycle status.'
    'runtime' = 'TrackSwap Runtime status.'
    'service' = 'Background service result or validation message.'
    'settings' = 'TrackSwap settings.'
    'startup' = 'Application startup and environment checks.'
    'status_bar' = 'Bottom status bar.'
    'steamvr' = 'SteamVR integration and maintenance.'
    'viewer' = 'TrackSwap VR picture viewer.'
}

function ConvertTo-CsvField([string]$Value) {
    if ($null -eq $Value) { $Value = '' }
    return '"' + $Value.Replace('"', '""') + '"'
}

function Get-RelativePath([string]$FullPath) {
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath($FullPath)
    if (-not $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return [IO.Path]::GetFileName($path)
    }
    return $path.Substring($root.Length).Replace('\', '/')
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('"identifier","source_phrase","translation","context"')
foreach ($key in ($catalog.strings.Keys | Sort-Object)) {
    $files = @($sourceFiles | Where-Object { $sourceTexts[$_.FullName].Contains($key) })
    if ($files.Count -eq 0) {
        throw "Localization key '$key' has no source reference."
    }

    $area = ($key -split '\.', 2)[0]
    $description = if ($areaDescriptions.ContainsKey($area)) {
        $areaDescriptions[$area]
    } else {
        'TrackSwap user interface text.'
    }
    $locations = @($files | ForEach-Object { Get-RelativePath $_.FullName } | Sort-Object -Unique)
    $context = $description + ' Used in: ' + [string]::Join(', ', $locations)
    $lines.Add(
        (ConvertTo-CsvField $key) + ',' +
        (ConvertTo-CsvField ([string]$catalog.strings[$key])) + ',' +
        (ConvertTo-CsvField '') + ',' +
        (ConvertTo-CsvField $context))
}

$fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($fullOutputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[IO.File]::WriteAllText(
    $fullOutputPath,
    [string]::Join([Environment]::NewLine, $lines) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Output "Exported $($catalog.strings.Count) Crowdin source strings to $fullOutputPath"
