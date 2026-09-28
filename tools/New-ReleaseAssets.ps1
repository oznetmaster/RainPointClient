# Copyright (c) 2026 Neil Colvin. MIT License.
# Builds local artifacts only. Never uploads, tags, logs in or operates a device.
[CmdletBinding()]
param([string]$Version = '1.0.0', [string]$OutputDirectory = 'artifacts/release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/Test-ReleaseMetadata.ps1" -Version $Version -Root $root
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Choose an empty release output directory to prevent stale assets.' }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $root
try {
    dotnet build RainPointClient.slnx -c Release -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    dotnet pack src/RainPointClient/RainPointClient.csproj -c Release --no-build -p:ContinuousIntegrationBuild=true -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }
    $packages = @(Get-ChildItem -LiteralPath $output -Filter '*.nupkg' -File)
    if ($packages.Count -ne 1) { throw 'Expected exactly one NuGet package.' }
    & "$PSScriptRoot/Test-ReleasePackage.ps1" -Package $packages[0].FullName -Version $Version
    foreach ($framework in @('net472','net10.0')) {
        $dll = Join-Path $root "src/RainPointClient/bin/Release/$framework/RainPointClient.dll"
        if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -cne ((($Version -split '-')[0]) + '.0')) { throw 'Assembly version differs from release version.' }
        foreach ($extension in @('dll','xml')) {
            Copy-Item -LiteralPath ([IO.Path]::ChangeExtension($dll,$extension)) -Destination (Join-Path $output "RainPointClient.$framework.$extension")
        }
    }
    foreach ($framework in @('net472','net10.0-windows')) {
        $folder = Join-Path $output "publish/RainPointClient.Desktop-$framework"
        dotnet publish src/RainPointClient.Desktop/RainPointClient.Desktop.csproj -c Release -f $framework --no-build --no-restore --self-contained false -o $folder
        if ($LASTEXITCODE -ne 0) { throw "Windows app preparation failed for $framework." }
        foreach ($document in @('LICENSE','THIRD-PARTY-NOTICES.md','README.md')) {
            Copy-Item -LiteralPath (Join-Path $root $document) -Destination $folder
        }
        Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $folder -Recurse
        if (!(Test-Path -LiteralPath (Join-Path $folder 'RainPointClient.Desktop.exe'))) { throw 'Windows launch executable missing.' }
        if (@(Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.Extension -in @('.trx','.pfx') -or $_.Name -match '(?i)rainpoint-test|\.local\.' }).Count) { throw 'Private/development files found in Windows output.' }
        Compress-Archive -Path (Join-Path $folder '*') -DestinationPath (Join-Path $output "RainPointClient.Desktop-$framework-$Version.zip")
    }
    foreach ($document in @('README.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md')) { Copy-Item -LiteralPath $document -Destination $output }
    Copy-Item -LiteralPath "release-notes/v$Version.md" -Destination (Join-Path $output 'RELEASE-NOTES.md')
    $manifest = @(Get-ChildItem -LiteralPath $output -File | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'),$manifest,[Text.UTF8Encoding]::new($false))
    Write-Output "Validated local release assets: $output"
} finally { Pop-Location }
