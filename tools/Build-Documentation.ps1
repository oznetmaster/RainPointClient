# Copyright (c) 2026 Neil Colvin. MIT License.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    # Remove generated output only; stale API pages must not survive a removed public API.
    foreach ($relative in @('docfx/api','docfx/.cache/references','artifacts/docs-site')) {
        $generated = [IO.Path]::GetFullPath((Join-Path $root $relative))
        if (!$generated.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Generated output escaped the repository.' }
        if (Test-Path -LiteralPath $generated) { Remove-Item -LiteralPath $generated -Recurse -Force }
    }
    dotnet build src/RainPointClient/RainPointClient.csproj -c Release -f net472 -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) { throw 'API documentation build failed.' }
    # DocFX reflection needs the .NET Framework facade assemblies, also on a modern SDK host.
    $assets = Get-Content src/RainPointClient/obj/project.assets.json -Raw | ConvertFrom-Json
    $referencePackage = $assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'Microsoft.NETFramework.ReferenceAssemblies.net472/*' } | Select-Object -First 1
    if (!$referencePackage) { throw 'Restored net472 reference package missing.' }
    $referenceRoot = $null
    foreach ($cache in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $cache ($referencePackage.Value.path + '/build/.NETFramework/v4.7.2')
        if (Test-Path -LiteralPath $candidate) { $referenceRoot = $candidate; break }
    }
    if (!$referenceRoot) { throw 'Cannot locate restored net472 reference assemblies.' }
    New-Item -ItemType Directory -Path docfx/.cache/references -Force | Out-Null
    Copy-Item src/RainPointClient/bin/Release/net472/*.dll -Destination docfx/.cache/references
    Copy-Item src/RainPointClient/bin/Release/net472/RainPointClient.xml -Destination docfx/.cache/references
    foreach ($assembly in @('System.Net.Http.dll','Facades/System.ValueTuple.dll')) {
        Copy-Item -LiteralPath (Join-Path $referenceRoot $assembly) -Destination docfx/.cache/references
    }
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Pinned DocFX restore failed.' }
    dotnet tool run docfx metadata docfx/docfx.json --warningsAsErrors
    if ($LASTEXITCODE -ne 0) { throw 'DocFX metadata failed.' }
    dotnet tool run docfx build docfx/docfx.json --warningsAsErrors
    if ($LASTEXITCODE -ne 0) { throw 'DocFX site build failed.' }
    foreach ($page in @('index.html','README.html','api/RainPointClient.RainPointCloudClient.html','docs/CALENDAR.html','release-notes/v1.0.1.html','tests/README.html')) {
        if (!(Test-Path -LiteralPath (Join-Path 'artifacts/docs-site' $page))) { throw "Missing documentation page: $page" }
    }
} finally { Pop-Location }
