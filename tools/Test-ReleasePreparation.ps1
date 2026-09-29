# Copyright (c) 2026 Neil Colvin. MIT License.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$root/.github/scripts/Test-RequiredReleaseChecks.ps1"
& "$PSScriptRoot/Test-ReleaseMetadata.ps1"
& "$PSScriptRoot/Test-SourceNotices.ps1"
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('rainpoint-release-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$temporary/src/RainPointClient","$temporary/release-notes","$temporary/tests" -Force | Out-Null
foreach ($file in @('Directory.Build.props','src/RainPointClient/RainPointClient.csproj','release-notes/v1.2.0.md','CHANGELOG.md','README.md','ATTRIBUTIONS.md','LICENSE','THIRD-PARTY-NOTICES.md','PUBLISHING.md','tests/README.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination (Join-Path $temporary $file)
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (!$rejected) { throw 'Release metadata regression accepted invalid input.' }
}
try {
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version '1.0.2' -Root $temporary }
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version '01.2.0' -Root $temporary }
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version "1.2.0`ninvalid" -Root $temporary }
    $projectPath = Join-Path $temporary 'src/RainPointClient/RainPointClient.csproj'
    $originalProject = Get-Content -LiteralPath $projectPath -Raw
    [IO.File]::WriteAllText($projectPath, $originalProject.Replace('/blob/v1.2.0/release-notes/v1.2.0.md','/blob/v0.0.0/release-notes/v0.0.0.md'))
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary }
    [IO.File]::WriteAllText($projectPath, $originalProject)
    Remove-Item -LiteralPath "$temporary/release-notes/v1.2.0.md"
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary }
    [IO.File]::WriteAllText("$temporary/release-notes/v1.2.0.md", "# RainPointClient 1.2.0`n`n")
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary }
    Copy-Item -LiteralPath "$root/release-notes/v1.2.0.md" -Destination "$temporary/release-notes/v1.2.0.md"
    [IO.File]::WriteAllText("$temporary/CHANGELOG.md",'# Changelog')
    Reject { & "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Root $temporary }
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
    Write-Output 'Release rejection tests: seven metadata and three source-notice scenarios passed.'

} finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (!$resolved.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or !([IO.Path]::GetFileName($resolved)).StartsWith('rainpoint-release-tests-')) { throw 'Unexpected temporary path; cleanup refused.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}