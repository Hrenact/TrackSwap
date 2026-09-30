param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$catalogPath = Join-Path $RepositoryRoot 'src\TrackSwap\Localization\zh-CN.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
$strings = $catalog.strings

function Get-LocalizationKey([string]$source) {
    foreach ($entry in $strings.GetEnumerator()) {
        if ([string]::Equals([string]$entry.Value, $source, [System.StringComparison]::Ordinal)) {
            return [string]$entry.Key
        }
    }
    throw "No semantic localization key exists for source text: $source. Add a descriptive flat key to zh-CN.json before running this migration."
}

$xamlFiles = @(
    'src\TrackSwap\MainWindow.xaml',
    'src\TrackSwap\BackupRestoreWindow.xaml'
)
$xamlPattern = '(?<name>Text|Content|Header|Title|ToolTip|Tag)="(?<value>[^"\r\n]*[\p{IsCJKUnifiedIdeographs}][^"\r\n]*)"'
foreach ($relativePath in $xamlFiles) {
    $path = Join-Path $RepositoryRoot $relativePath
    $text = Get-Content -LiteralPath $path -Raw
    if ($text -notmatch 'xmlns:i18n=') {
        $text = $text -replace 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"', 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:i18n="clr-namespace:TrackSwap.Localization"'
    }
    $text = [regex]::Replace($text, $xamlPattern, {
        param($match)
        $source = [System.Net.WebUtility]::HtmlDecode($match.Groups['value'].Value)
        $key = Get-LocalizationKey $source
        return $match.Groups['name'].Value + '="{i18n:Loc Key=' + $key + '}"'
    })
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}

$codeFiles = @(
    'src\TrackSwap\MainWindow.xaml.cs',
    'src\TrackSwap\BackupRestoreWindow.xaml.cs',
    'src\TrackSwap\Services\ConfigurationBackupService.cs',
    'src\TrackSwap\Services\DataMigrationService.cs',
    'src\TrackSwap\Services\DiagnosticsService.cs',
    'src\TrackSwap\Services\OpenVrDeviceService.cs',
    'src\TrackSwap\Services\OpenVrInterop.cs',
    'src\TrackSwap\Services\OpenVrRenderModelService.cs',
    'src\TrackSwap\Services\RuntimeControlService.cs',
    'src\TrackSwap\Services\SteamIntegrationMaintenanceService.cs',
    'src\TrackSwap\Services\SteamVrApplicationService.cs',
    'src\TrackSwap\Services\SteamVrSettingsService.cs'
)
$literalPattern = '(?<![$@])"(?<value>(?:\\.|[^"\\])*[\p{IsCJKUnifiedIdeographs}](?:\\.|[^"\\])*)"'
foreach ($relativePath in $codeFiles) {
    $path = Join-Path $RepositoryRoot $relativePath
    $text = Get-Content -LiteralPath $path -Raw
    if ($text -notmatch 'using TrackSwap\.Localization;') {
        $usingPattern = [regex]::new('(?m)^(using [^;]+;\r?\n)')
        $text = $usingPattern.Replace(
            $text,
            '$1using TrackSwap.Localization;' + [Environment]::NewLine,
            1)
    }
    $text = [regex]::Replace($text, $literalPattern, {
        param($match)
        $source = [regex]::Unescape($match.Groups['value'].Value)
        $key = Get-LocalizationKey $source
        return 'Tr.Get("' + $key + '")'
    })
    [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
}
