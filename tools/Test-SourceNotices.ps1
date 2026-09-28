# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
[CmdletBinding()]
param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$files = @()
foreach ($directory in @('src','samples','tests')) {
    $path = Join-Path $Root $directory
    if (Test-Path -LiteralPath $path) {
        $files += @(Get-ChildItem -LiteralPath $path -Recurse -File | Where-Object {
            $_.Extension -in @('.cs','.xaml','.csproj') -and $_.FullName -notmatch '[\\/](bin|obj|\.local)[\\/]'
        })
    }
}
foreach ($name in @('Directory.Build.props','RainPointClient.slnx')) {
    $path = Join-Path $Root $name
    if (Test-Path -LiteralPath $path) { $files += Get-Item -LiteralPath $path }
}
if (!$files.Count) { throw 'No authored source files found for notice validation.' }
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
    $header = ($text -split '\r?\n' | Select-Object -First 6) -join "`n"
    if ($header -notmatch 'Copyright (©|\(c\)) 2026 Neil Colvin' -or $header -notmatch 'Licensed under the MIT License' -or $header -notmatch 'See LICENSE') {
        throw "Missing source copyright/license header: $($file.FullName)"
    }
}
$readme = Get-Content -LiteralPath (Join-Path $Root 'README.md') -Raw -Encoding utf8
foreach ($required in @('## Attributions','## Trademarks and disclaimer','## License','not affiliated with','without warranty','ATTRIBUTIONS.md','THIRD-PARTY-NOTICES.md')) {
    if (!$readme.Contains($required)) { throw "README notice missing: $required" }
}
$attributions = Get-Content -LiteralPath (Join-Path $Root 'ATTRIBUTIONS.md') -Raw -Encoding utf8
foreach ($source in @('funkadelic/ha-rainpoint','brettmeyerowitz/homeassistant-homgar','Remboooo/homgarapi','macher91/homgar-homeassistant','rathga/rainpoint-ha')) {
    if (!$attributions.Contains($source)) { throw "Missing upstream attribution: $source" }
}
Write-Output "Source notices verified: $($files.Count) authored files; README and five upstream attributions present."
