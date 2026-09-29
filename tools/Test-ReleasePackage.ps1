# Copyright (c) 2026 Neil Colvin. MIT License.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Package, [string]$Version = '1.1.0')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package))
try {
    $names = @($zip.Entries | ForEach-Object { $_.FullName })
    foreach ($name in @('RainPointClient.nuspec','licenses/MQTTnet-LICENSE.txt','licenses/Microsoft-Runtime-LICENSE.txt','lib/net472/RainPointClient.dll','lib/net472/RainPointClient.xml','lib/net10.0/RainPointClient.dll','lib/net10.0/RainPointClient.xml','README.md','ATTRIBUTIONS.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md',"release-notes/v$Version.md")) {
        if ($name -cnotin $names) { throw "Missing package entry: $name" }
    }
    foreach ($name in $names) {
        if ($name -match '(?i)(^|/)(\.local|artifacts|tests|samples|obj|bin)/|\.(trx|pcap|jsonl|pfx|key)$|RainPointClient\.(Tests|Desktop)') { throw "Unexpected private/development package entry: $name" }
    }
    $reader = [IO.StreamReader]::new($zip.GetEntry('RainPointClient.nuspec').Open())
    try { [xml]$spec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $spec.package.metadata
    if ($metadata.id -cne 'RainPointClient' -or $metadata.version -cne $Version -or $metadata.readme -cne 'README.md' -or $metadata.license.'#text' -cne 'MIT') { throw 'Invalid package identity, version, README or license.' }
    if ($metadata.copyright -notmatch '2026 Neil Colvin') { throw 'Package copyright metadata missing.' }
    $reader = [IO.StreamReader]::new($zip.GetEntry('README.md').Open())
    try { $readme = $reader.ReadToEnd() } finally { $reader.Dispose() }
    foreach ($required in @('## Attributions','## Trademarks and disclaimer','## License','without warranty','ATTRIBUTIONS.md')) {
        if (!$readme.Contains($required)) { throw "Packaged README notice missing: $required" }
    }
    $groups = @($metadata.dependencies.group)
    if ((@($groups | ForEach-Object { $_.targetFramework } | Sort-Object) -join ';') -cne '.NETFramework4.7.2;net10.0') { throw 'Unexpected package frameworks.' }
    foreach ($group in $groups) {
        $dependencies = @($group.dependency | ForEach-Object { $_.id } | Sort-Object)
        $expectedIds = if ($group.targetFramework -eq 'net10.0') { 'MQTTnet' } else { 'MQTTnet,System.Text.Json' }
        if (($dependencies -join ',') -cne $expectedIds) { throw 'Unexpected direct package dependencies.' }
        foreach ($dependency in $group.dependency) {
            $expected = if ($dependency.id -eq 'MQTTnet') { '4.3.7.1207' } else { '10.0.12' }
            if ($dependency.version -notin @($expected,"[$expected, )")) { throw 'Dependency version differs from reviewed stable baseline.' }
        }
    }
    Write-Output "Package contents validated: RainPointClient $Version; both targets, API XML and release documents present."
} finally { $zip.Dispose() }
