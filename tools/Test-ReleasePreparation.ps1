# Copyright (c) 2026 Neil Colvin. MIT License.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$root/.github/scripts/Test-RequiredReleaseChecks.ps1"
& "$PSScriptRoot/Test-ReleaseMetadata.ps1"
& "$PSScriptRoot/Test-SourceNotices.ps1"
dotnet run --project "$PSScriptRoot/Documentation/Documentation.csproj" -c Release -- --self-test
if ($LASTEXITCODE -ne 0) { throw 'Documentation policy tests failed.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('rainpoint-release-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$temporary/src/RainPointClient","$temporary/release-notes","$temporary/tests" -Force | Out-Null
foreach ($file in @('Directory.Build.props','src/RainPointClient/RainPointClient.csproj','release-notes/v1.2.1.md','CHANGELOG.md','README.md','ATTRIBUTIONS.md','LICENSE','THIRD-PARTY-NOTICES.md','PUBLISHING.md','tests/README.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $temporary $file)
}
# Keep the original 1.2.1 fixture independent of the next public version.
$fixtureProps = Join-Path $temporary 'Directory.Build.props'
$fixturePropsText = Get-Content -LiteralPath $fixtureProps -Raw
$fixturePropsText = [regex]::Replace($fixturePropsText, '<Version>[^<]+</Version>', '<Version>1.2.1</Version>')
$fixturePropsText = [regex]::Replace($fixturePropsText, '<AssemblyVersion>[^<]+</AssemblyVersion>', '<AssemblyVersion>1.2.1.0</AssemblyVersion>')
$fixturePropsText = [regex]::Replace($fixturePropsText, '<FileVersion>[^<]+</FileVersion>', '<FileVersion>1.2.1.0</FileVersion>')
[IO.File]::WriteAllText($fixtureProps, $fixturePropsText)
$fixtureProject = Join-Path $temporary 'src/RainPointClient/RainPointClient.csproj'
$fixtureProjectText = Get-Content -LiteralPath $fixtureProject -Raw
$fixtureProjectText = [regex]::Replace($fixtureProjectText, '/blob/v[0-9.]+/release-notes/v[0-9.]+\.md', '/blob/v1.2.1/release-notes/v1.2.1.md')
[IO.File]::WriteAllText($fixtureProject, $fixtureProjectText)
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Release metadata regression accepted invalid input.' }
}
try {
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version '1.0.2' -Root $temporary }
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version '01.2.1' -Root $temporary }
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version "1.2.1`ninvalid" -Root $temporary }
    $projectPath = Join-Path $temporary 'src/RainPointClient/RainPointClient.csproj'
    $originalProject = Get-Content -LiteralPath $projectPath -Raw
    # Unrelated policy groups must not change how unique identity properties are read.
    [IO.File]::WriteAllText($projectPath, $originalProject.Replace('</Project>', '<PropertyGroup><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup><PropertyGroup><WarningsAsErrors>CS1591</WarningsAsErrors></PropertyGroup></Project>'))
    & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' | Out-Null
    foreach ($invalidProject in @(
        $originalProject.Replace('<PackageId>RainPointClient</PackageId>', '<PackageId>WrongClient</PackageId>'),
        $originalProject.Replace('<TargetFrameworks>net472;net10.0</TargetFrameworks>', '<TargetFrameworks>net10.0</TargetFrameworks>'),
        $originalProject.Replace('</Project>', '<PropertyGroup><PackageId>RainPointClient</PackageId></PropertyGroup></Project>'),
        $originalProject.Replace('</Project>', '<PropertyGroup><TargetFrameworks>net472;net10.0</TargetFrameworks></PropertyGroup></Project>')
    )) {
        [IO.File]::WriteAllText($projectPath, $invalidProject)
        Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    }
    [IO.File]::WriteAllText($projectPath, $originalProject.Replace('/blob/v1.2.1/release-notes/v1.2.1.md','/blob/v0.0.0/release-notes/v0.0.0.md'))
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    [IO.File]::WriteAllText($projectPath, $originalProject)
    Remove-Item -LiteralPath "$temporary/release-notes/v1.2.1.md"
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    [IO.File]::WriteAllText("$temporary/release-notes/v1.2.1.md", "# RainPointClient 1.2.1`n`n")
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    Copy-Item -LiteralPath "$root/release-notes/v1.2.1.md" -Destination "$temporary/release-notes/v1.2.1.md"
    [IO.File]::WriteAllText("$temporary/CHANGELOG.md",'# Changelog')
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    foreach ($heading in @('## 1.2.1 — 2026-09-29','## [1.2.1] - 2026-09-29','## [1.2.1](https://github.com/oznetmaster/RainPointClient/releases/tag/v1.2.1) - 2026-09-29')) {
        [IO.File]::WriteAllText("$temporary/CHANGELOG.md", "# Changelog`n`n$heading`n`nValid entry.`n")
        & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' | Out-Null
    }
    foreach ($heading in @('## 1.2.10 — 2026-09-29','## [1.2.10] - 2026-09-29','## [1.2.1 - 2026-09-29','### 1.2.1 - 2026-09-29','## [1.2.10](https://github.com/oznetmaster/RainPointClient/releases/tag/v1.2.1) - 2026-09-29','## [1.2.1](https://example.invalid - 2026-09-29')) {
        [IO.File]::WriteAllText("$temporary/CHANGELOG.md", "# Changelog`n`n$heading`n`nInvalid entry.`n")
        Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary -Version '1.2.1' }
    }
    Copy-Item -LiteralPath "$root/CHANGELOG.md" -Destination "$temporary/CHANGELOG.md"
    & "$PSScriptRoot/Test-SourceNotices.ps1" -Root $temporary | Out-Null
    [IO.File]::WriteAllText("$temporary/src/RainPointClient/MissingHeader.cs",'class MissingHeader {}')
    Reject { & "$PSScriptRoot/Test-SourceNotices.ps1" -Root $temporary }
    Remove-Item -LiteralPath "$temporary/src/RainPointClient/MissingHeader.cs"
    [IO.File]::WriteAllText("$temporary/README.md",'# README')
    Reject { & "$PSScriptRoot/Test-SourceNotices.ps1" -Root $temporary }
    Copy-Item -LiteralPath "$root/README.md" -Destination "$temporary/README.md"
    [IO.File]::WriteAllText("$temporary/ATTRIBUTIONS.md",'# Attributions')
    Reject { & "$PSScriptRoot/Test-SourceNotices.ps1" -Root $temporary }
    Write-Output 'Release checks: three changelog formats and unrelated property groups accepted; seventeen metadata and three source-notice rejection scenarios passed.'

} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('rainpoint-release-tests-')) { throw 'Unexpected temporary path; cleanup refused.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
