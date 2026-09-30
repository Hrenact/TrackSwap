param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'localization\packs'),
    [string]$Locale,
    [string]$DisplayName,
    [string]$Author = 'TrackSwap Crowdin Community'
)

$ErrorActionPreference = 'Stop'

$catalogPath = Join-Path $RepositoryRoot 'src\TrackSwap\Localization\zh-CN.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
$rows = @(Import-Csv -LiteralPath $InputPath)
if ($rows.Count -eq 0) {
    throw 'The Crowdin translation file is empty.'
}

$columns = @($rows[0].PSObject.Properties.Name)
if (-not ($columns -contains 'identifier') -or
    -not ($columns -contains 'source_phrase') -or
    -not ($columns -contains 'translation')) {
    throw 'The Crowdin CSV must contain identifier, source_phrase, and translation columns.'
}

if ([string]::IsNullOrWhiteSpace($Locale)) {
    $Locale = [IO.Path]::GetFileNameWithoutExtension($InputPath)
}
if ($Locale -notmatch '^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$') {
    throw "'$Locale' is not a supported locale identifier."
}
if ([string]::Equals($Locale, 'zh-CN', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The built-in Simplified Chinese catalog cannot be replaced by a community pack.'
}
if ([string]::IsNullOrWhiteSpace($DisplayName)) {
    try {
        $DisplayName = [Globalization.CultureInfo]::GetCultureInfo($Locale).NativeName
    } catch {
        $DisplayName = $Locale
    }
}

function Get-Placeholders([string]$Value) {
    if ($null -eq $Value) { return @() }
    return @([regex]::Matches($Value, '\{\d+(?:,[^}:]+)?(?::[^}]+)?\}') |
        ForEach-Object Value |
        Sort-Object -Unique)
}

function ConvertTo-JsonLiteral([object]$Value) {
    return ConvertTo-Json $Value -Compress
}

$translations = [ordered]@{}
$staleKeys = [Collections.Generic.List[string]]::new()
$invalidPlaceholders = [Collections.Generic.List[string]]::new()
foreach ($row in $rows) {
    $key = [string]$row.identifier
    $translation = [string]$row.translation
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    if (-not $catalog.strings.Contains($key)) {
        $staleKeys.Add($key)
        continue
    }
    if ($translations.Contains($key)) {
        throw "The Crowdin translation contains duplicate key '$key'."
    }
    if ([string]::IsNullOrWhiteSpace($translation)) { continue }

    $sourcePlaceholders = @(Get-Placeholders ([string]$catalog.strings[$key]))
    $translationPlaceholders = @(Get-Placeholders $translation)
    if ([string]::Join('|', $sourcePlaceholders) -ne [string]::Join('|', $translationPlaceholders)) {
        $invalidPlaceholders.Add($key)
        continue
    }
    $translations[$key] = $translation
}

if ($staleKeys.Count -gt 0) {
    throw 'The Crowdin translation contains stale keys: ' + [string]::Join(', ', $staleKeys)
}
if ($invalidPlaceholders.Count -gt 0) {
    throw 'Placeholder mismatch in keys: ' + [string]::Join(', ', $invalidPlaceholders)
}

$missingKeys = @($catalog.strings.Keys | Where-Object { -not $translations.Contains($_) } | Sort-Object)
$keys = @($translations.Keys | Sort-Object)
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('{')
$lines.Add('  "schemaVersion": 1,')
$lines.Add('  "locale": ' + (ConvertTo-JsonLiteral $Locale) + ',')
$lines.Add('  "displayName": ' + (ConvertTo-JsonLiteral $DisplayName) + ',')
$lines.Add('  "author": ' + (ConvertTo-JsonLiteral $Author) + ',')
$lines.Add('  "targetTrackSwapVersion": ' + (ConvertTo-JsonLiteral $catalog.targetTrackSwapVersion) + ',')
$lines.Add('  "strings": {')
$previousGroup = $null
for ($index = 0; $index -lt $keys.Count; $index++) {
    $key = [string]$keys[$index]
    $group = ($key -split '\.', 2)[0]
    if ($null -ne $previousGroup -and $group -ne $previousGroup) { $lines.Add('') }
    $comma = if ($index -lt $keys.Count - 1) { ',' } else { '' }
    $lines.Add('    ' + (ConvertTo-JsonLiteral $key) + ': ' + (ConvertTo-JsonLiteral $translations[$key]) + $comma)
    $previousGroup = $group
}
$lines.Add('  }')
$lines.Add('}')

$fullOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($fullOutputDirectory) | Out-Null
$outputPath = Join-Path $fullOutputDirectory ($Locale + '.json')
[IO.File]::WriteAllText(
    $outputPath,
    [string]::Join([Environment]::NewLine, $lines) + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false))

Write-Output "Imported $($translations.Count) translations to $outputPath"
if ($missingKeys.Count -gt 0) {
    Write-Warning "$($missingKeys.Count) catalog keys are untranslated and were omitted. TrackSwap will display their fallback keys."
}
