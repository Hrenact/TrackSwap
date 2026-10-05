[CmdletBinding()]
param(
    [string]$RuntimeDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$noticePath = Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md'
$errors = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
    throw "Third-party notice audit failed:`n - Missing THIRD-PARTY-NOTICES.md."
}

$notice = Get-Content -LiteralPath $noticePath -Raw

function Add-AuditError([string]$Message) {
    $script:errors.Add($Message)
}

function Test-NoticeText([string]$Text, [string]$Description) {
    if ($notice.IndexOf($Text, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-AuditError "THIRD-PARTY-NOTICES.md is missing or outdated: $Description (expected '$Text')."
    }
}

# Every direct managed dependency in production sources or distributable tools
# must name both the package and its exact declared version.
$projectFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.csproj' -Recurse -File
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'tools') -Filter '*.csproj' -Recurse -File -ErrorAction SilentlyContinue
)
foreach ($projectFile in $projectFiles) {
    [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
    foreach ($reference in @($project.Project.ItemGroup.PackageReference)) {
        if ($null -eq $reference) { continue }
        $packageName = [string]$reference.Include
        $packageVersion = [string]$reference.Version
        if ([string]::IsNullOrWhiteSpace($packageName)) { continue }
        Test-NoticeText $packageName "managed package used by $($projectFile.FullName.Substring($repositoryRoot.Length + 1))"
        if (-not [string]::IsNullOrWhiteSpace($packageVersion)) {
            Test-NoticeText $packageVersion "version of managed package $packageName"
        }
    }
}

# Verify every Git-backed native dependency and the revision stated in the notice.
$driverCMakePath = Join-Path $repositoryRoot 'src\TrackSwap.Driver\CMakeLists.txt'
$driverCMake = Get-Content -LiteralPath $driverCMakePath -Raw
$fetchBlocks = [regex]::Matches(
    $driverCMake,
    'FetchContent_Declare\(\s*(\S+)(.*?)\)',
    [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
foreach ($fetchBlock in $fetchBlocks) {
    $dependencyName = $fetchBlock.Groups[1].Value
    $block = $fetchBlock.Groups[2].Value
    $repositoryMatch = [regex]::Match($block, 'GIT_REPOSITORY\s+(\S+)', 'IgnoreCase')
    if (-not $repositoryMatch.Success) { continue }

    $repository = $repositoryMatch.Groups[1].Value.Trim('"').TrimEnd('/')
    $repositoryNoticeText = $repository -replace '\.git$', ''
    Test-NoticeText $repositoryNoticeText "native dependency $dependencyName"

    $tagMatch = [regex]::Match($block, 'GIT_TAG\s+(\S+)', 'IgnoreCase')
    if (-not $tagMatch.Success) {
        Add-AuditError "Native dependency $dependencyName has no pinned GIT_TAG in src/TrackSwap.Driver/CMakeLists.txt."
        continue
    }
    $revision = $tagMatch.Groups[1].Value.Trim('"')
    $variableMatch = [regex]::Match($revision, '^\$\{([^}]+)\}$')
    if ($variableMatch.Success) {
        $variableName = $variableMatch.Groups[1].Value
        $valueMatch = [regex]::Match(
            $driverCMake,
            "set\(\s*$([regex]::Escape($variableName))\s+`"([^`"]+)`"",
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $valueMatch.Success) {
            Add-AuditError "Cannot resolve GIT_TAG variable $variableName for native dependency $dependencyName."
            continue
        }
        $revision = $valueMatch.Groups[1].Value
    }
    Test-NoticeText $revision "pinned revision of native dependency $dependencyName"
}

# Third-party asset collections require a matching source notice.
$thirdPartyAssets = Join-Path $repositoryRoot 'src\TrackSwap\Assets\ThirdParty'
if (Test-Path -LiteralPath $thirdPartyAssets -PathType Container) {
    foreach ($assetCollection in Get-ChildItem -LiteralPath $thirdPartyAssets -Directory) {
        Test-NoticeText $assetCollection.Name "third-party asset collection $($assetCollection.Name)"
    }
}

# These are reference/interoperation disclosures which are not discoverable as
# package references but are part of the current device-viewer implementation.
if ((Test-Path -LiteralPath (Join-Path $repositoryRoot 'src\TrackSwap\DeviceViewerModelFactory.cs')) -or
    (Test-Path -LiteralPath (Join-Path $repositoryRoot 'src\TrackSwap\Services\OpenVrBackgroundService.cs'))) {
    Test-NoticeText '## SteamVR' 'SteamVR behavior and local-resource interoperation disclosure'
}
Test-NoticeText '## Microsoft .NET Runtime' 'self-contained Microsoft .NET Runtime disclosure'

if (-not [string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    $resolvedRuntimeDirectory = [System.IO.Path]::GetFullPath($RuntimeDirectory)
    $runtimeConfigPath = Join-Path $resolvedRuntimeDirectory 'TrackSwap.Runtime.runtimeconfig.json'
    if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
        Add-AuditError "Published Runtime configuration is missing: $runtimeConfigPath"
    } else {
        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
        $framework = @($runtimeConfig.runtimeOptions.includedFrameworks) |
            Where-Object { $_.name -eq 'Microsoft.NETCore.App' } |
            Select-Object -First 1
        if ($null -eq $framework -or [string]::IsNullOrWhiteSpace([string]$framework.version)) {
            Add-AuditError 'Cannot determine the Microsoft.NETCore.App version from the published Runtime configuration.'
        } else {
            $runtimeVersion = [string]$framework.version
            $packageRoots = [System.Collections.Generic.List[string]]::new()
            if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
                $packageRoots.Add($env:NUGET_PACKAGES)
            }
            $assetsPath = Join-Path $repositoryRoot 'src\TrackSwap.Runtime\obj\project.assets.json'
            if (Test-Path -LiteralPath $assetsPath -PathType Leaf) {
                $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
                foreach ($property in $assets.packageFolders.PSObject.Properties) {
                    $packageRoots.Add($property.Name.TrimEnd('\', '/'))
                }
            }
            $packageRoots.Add((Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages'))

            $runtimePackDirectory = $null
            foreach ($packageRoot in $packageRoots | Select-Object -Unique) {
                $candidate = Join-Path $packageRoot "microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
                if (Test-Path -LiteralPath $candidate -PathType Container) {
                    $runtimePackDirectory = $candidate
                    break
                }
            }
            if ($null -eq $runtimePackDirectory) {
                Add-AuditError "Cannot find Microsoft.NETCore.App.Runtime.win-x64 $runtimeVersion in the restored NuGet package folders."
            } else {
                foreach ($filePair in @(
                    @{ Source = 'LICENSE.TXT'; Destination = 'DOTNET-LICENSE.txt' },
                    @{ Source = 'THIRD-PARTY-NOTICES.TXT'; Destination = 'DOTNET-THIRD-PARTY-NOTICES.txt' }
                )) {
                    $sourcePath = Join-Path $runtimePackDirectory $filePair.Source
                    $destinationPath = Join-Path $resolvedRuntimeDirectory $filePair.Destination
                    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                        Add-AuditError "The resolved .NET Runtime pack is missing $sourcePath."
                    } elseif (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
                        Add-AuditError "The release is missing runtime/$($filePair.Destination) for Microsoft.NETCore.App.Runtime.win-x64 $runtimeVersion."
                    } elseif ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne
                              (Get-FileHash -LiteralPath $destinationPath -Algorithm SHA256).Hash) {
                        Add-AuditError "runtime/$($filePair.Destination) does not match Microsoft.NETCore.App.Runtime.win-x64 $runtimeVersion."
                    }
                }
            }
        }
    }
}

if ($errors.Count -gt 0) {
    throw "Third-party notice audit failed. Stop the release and report these required updates to the user:`n - $($errors -join "`n - ")"
}

Write-Host 'Third-party notice audit passed.'
