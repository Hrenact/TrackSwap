$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixtureRoot = Join-Path $env:TEMP ('TrackSwap-crowdin-test-' + [Guid]::NewGuid().ToString('N'))
$sourcePath = Join-Path $fixtureRoot 'source.csv'
$translationPath = Join-Path $fixtureRoot 'en-US.csv'
$packDirectory = Join-Path $fixtureRoot 'packs'

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    $crowdinConfiguration = Get-Content -LiteralPath (Join-Path $repositoryRoot 'crowdin.yml') -Raw
    if ($crowdinConfiguration -notmatch '(?m)^\s+skip_untranslated_strings:\s*true\s*$') {
        throw 'Crowdin must skip untranslated strings instead of exporting Chinese source fallback text.'
    }

    & (Join-Path $repositoryRoot 'scripts\Export-CrowdinSource.ps1') `
        -RepositoryRoot $repositoryRoot `
        -OutputPath $sourcePath | Out-Null

    $sourceRows = @(Import-Csv -LiteralPath $sourcePath)
    if ($sourceRows.Count -eq 0) {
        throw 'Crowdin exporter produced no source rows.'
    }
    $sourceColumns = @($sourceRows[0].PSObject.Properties.Name)
    if ([string]::Join(',', $sourceColumns) -ne 'identifier,source_phrase,translation,context') {
        throw "Unexpected Crowdin source columns: $([string]::Join(',', $sourceColumns))"
    }
    if (@($sourceRows | Where-Object { -not [string]::IsNullOrEmpty($_.translation) }).Count -ne 0) {
        throw 'Crowdin source export must leave every translation cell empty.'
    }

    $catalogPath = Join-Path $repositoryRoot 'src\TrackSwap\Localization\zh-CN.json'
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
    $keys = @($catalog.strings.Keys | Sort-Object | Select-Object -First 2)
    $translatedKey = [string]$keys[0]
    $untranslatedKey = [string]$keys[1]
    @(
        [pscustomobject]@{
            identifier = $translatedKey
            source_phrase = [string]$catalog.strings[$translatedKey]
            translation = [string]$catalog.strings[$translatedKey]
            context = 'Intentional same-as-source translation.'
        },
        [pscustomobject]@{
            identifier = $untranslatedKey
            source_phrase = [string]$catalog.strings[$untranslatedKey]
            translation = ''
            context = 'Untranslated fixture.'
        }
    ) | Export-Csv -LiteralPath $translationPath -NoTypeInformation -Encoding UTF8

    & (Join-Path $repositoryRoot 'scripts\Import-CrowdinTranslation.ps1') `
        -InputPath $translationPath `
        -RepositoryRoot $repositoryRoot `
        -OutputDirectory $packDirectory `
        -Locale 'en-US' | Out-Null

    $pack = Get-Content -LiteralPath (Join-Path $packDirectory 'en-US.json') -Raw |
        ConvertFrom-Json -AsHashtable
    if (-not $pack.strings.Contains($translatedKey) -or
        [string]$pack.strings[$translatedKey] -ne [string]$catalog.strings[$translatedKey]) {
        throw 'Importer did not preserve an intentional same-as-source translation.'
    }
    if ($pack.strings.Contains($untranslatedKey)) {
        throw 'Importer must omit a row whose translation column is empty.'
    }

    Write-Output 'Crowdin localization fixture tests passed.'
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
