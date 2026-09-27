[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][ValidatePattern('^v?\d+\.\d+\.\d+(\.\d+)?$')][string]$ReleaseVersion,
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$AssemblyVersion,
    [string]$Repository = '',
    [string]$Commit = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) { throw "Plugin package not found: $PackagePath" }
$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $packageEntries = foreach ($entry in $archive.Entries | Sort-Object FullName) {
        $stream = $entry.Open()
        try {
            [ordered]@{
                Name = $entry.FullName
                Size = [long]$entry.Length
                CompressedSize = [long]$entry.CompressedLength
                Sha256 = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        finally { $stream.Dispose() }
    }
}
finally { $archive.Dispose() }

$packageInfo = Get-Item -LiteralPath $resolvedPackage
$checksum = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
$inventory = [ordered]@{
    SchemaVersion = 1
    Product = 'MobGrinder'
    ReleaseVersion = $ReleaseVersion
    AssemblyVersion = $AssemblyVersion
    Repository = $Repository
    Commit = $Commit
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Artifacts = @([ordered]@{ Name = $packageInfo.Name; Size = [long]$packageInfo.Length; Sha256 = $checksum; MediaType = 'application/zip' })
    PackageEntries = @($packageEntries)
}
Set-Content -LiteralPath (Join-Path $resolvedOutput 'SHA256SUMS.txt') -Encoding UTF8 -Value "$checksum  $($packageInfo.Name)"
Set-Content -LiteralPath (Join-Path $resolvedOutput 'RELEASE-MANIFEST.json') -Encoding UTF8 -Value (ConvertTo-Json $inventory -Depth 10)
Write-Host "Release metadata generated: $resolvedOutput"
