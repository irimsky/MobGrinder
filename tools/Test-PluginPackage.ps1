[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,

    [string]$PluginManifestPath = (Join-Path $PSScriptRoot '..\MobGrinder\MobGrinder.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Plugin package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "mobgrinder-package-$([Guid]::NewGuid().ToString('N'))"

try {
    $entries = @($archive.Entries)
    $files = @($entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $names = @($files | ForEach-Object FullName)
    $duplicates = @($names | Group-Object | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        throw "The plugin contains duplicate ZIP entries: $($duplicates.Name -join ', ')"
    }

    foreach ($name in $names) {
        if ($name.StartsWith('/') -or $name.Contains('..') -or $name.Contains('\\')) {
            throw "The plugin package contains an invalid path: $name"
        }
        if ($name -match '(^|/)(AGENTS|HANDOFF|Dalamud|FFXIVClientStructs|TestResults)([^/]*)') {
            throw "The plugin package contains a forbidden file: $name"
        }
    }

    $requiredNames = @('MobGrinder.dll', 'MobGrinder.json')
    foreach ($requiredName in $requiredNames) {
        if ($names -notcontains $requiredName) {
            throw "The plugin package is missing the ZIP root file: $requiredName"
        }
    }

    $manifestEntry = $archive.GetEntry('MobGrinder.json')
    $manifest = [System.IO.StreamReader]::new($manifestEntry.Open()).ReadToEnd() | ConvertFrom-Json
    if ($manifest.InternalName -ne 'MobGrinder') {
        throw "The package manifest has an incorrect InternalName: $($manifest.InternalName)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.AssemblyVersion)) {
        throw 'The package manifest is missing AssemblyVersion.'
    }

    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
    $dllPath = Join-Path $temporaryDirectory 'MobGrinder.dll'
    $dllStream = [IO.File]::Create($dllPath)
    try { $archive.GetEntry('MobGrinder.dll').Open().CopyTo($dllStream) }
    finally { $dllStream.Dispose() }
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($dllPath).Version.ToString()
    if ($assemblyVersion -ne [string]$manifest.AssemblyVersion) {
        throw "DLL version $assemblyVersion does not match package manifest version $($manifest.AssemblyVersion)."
    }

    if (Test-Path -LiteralPath $PluginManifestPath -PathType Leaf) {
        $source = Get-Content -LiteralPath $PluginManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($source.InternalName -ne $manifest.InternalName) {
            throw 'The package and source manifests have different InternalName values.'
        }
    }

    Write-Host "Plugin package verification passed: $resolvedPackage"
    Write-Host "AssemblyVersion=$assemblyVersion; files=$($files.Count)"
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
