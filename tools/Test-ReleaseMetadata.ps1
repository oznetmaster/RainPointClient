# Copyright (c) 2026 Neil Colvin. MIT License.
[CmdletBinding()]
param([string]$Version = '1.0.1', [string]$Root = (Split-Path $PSScriptRoot -Parent), [switch]$RequireTag)
$ErrorActionPreference = 'Stop'
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Use a canonical three-part package version, optionally with a prerelease suffix.' }
[xml]$props = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
$numeric = ($Version -split '-')[0]
foreach ($name in @('Version','AssemblyVersion','FileVersion')) {
    $expected = if ($name -eq 'Version') { $Version } else { "$numeric.0" }
    $actual = $props.SelectSingleNode("/Project/PropertyGroup/$name")
    if ($null -eq $actual -or $actual.InnerText -cne $expected) { throw "Set $name to '$expected' before publishing." }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $Root 'src/RainPointClient/RainPointClient.csproj') -Raw
if ($project.Project.PropertyGroup.PackageId -cne 'RainPointClient' -or $project.Project.PropertyGroup.TargetFrameworks -cne 'net472;net10.0') { throw 'Package identity/targets changed; review release validation.' }
if ($project.SelectSingleNode('/Project/PropertyGroup/Version')) { throw 'The package must inherit the shared release version.' }
if ($project.Project.PropertyGroup.PackageReleaseNotes -notmatch ([regex]::Escape("/blob/v$Version/release-notes/v$Version.md"))) { throw 'Package release-note URL does not match the version.' }
$notes = Join-Path $Root "release-notes/v$Version.md"
if (!(Test-Path -LiteralPath $notes -PathType Leaf)) { throw "Missing versioned release notes: v$Version." }
$notesText = Get-Content -LiteralPath $notes -Raw
$notesLines = $notesText -split "\r?\n", 2
if ($notesLines[0] -cne "# RainPointClient $Version" -or $notesLines.Count -lt 2 -or [string]::IsNullOrWhiteSpace($notesLines[1])) { throw 'Release notes are missing or inconsistent.' }
$changelog = Get-Content -LiteralPath (Join-Path $Root 'CHANGELOG.md') -Raw
if ($changelog -notmatch ('(?m)^## \[' + [regex]::Escape($Version) + '\]')) { throw 'No changelog entry matches this version.' }
foreach ($file in @('README.md','ATTRIBUTIONS.md','LICENSE','THIRD-PARTY-NOTICES.md','PUBLISHING.md','tests/README.md')) {
    if (!(Test-Path -LiteralPath (Join-Path $Root $file) -PathType Leaf)) { throw "Missing release document: $file" }
}
if ($RequireTag) {
    $head = git -C $Root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify release commit.' }
    $tag = git -C $Root rev-parse --verify "refs/tags/v${Version}^{commit}"
    if ($LASTEXITCODE -ne 0 -or $head -cne $tag) { throw 'The checked-out commit must match the existing version tag.' }
}
Write-Output "Release metadata validated: RainPointClient $Version ($numeric.0)."
