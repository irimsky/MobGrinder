[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$PluginManifestPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^https://github\.com/[^/]+/[^/]+$')][string]$RepositoryUrl,
    [Parameter(Mandatory = $true)][ValidatePattern('^v?\d+\.\d+\.\d+(\.\d+)?$')][string]$AssemblyVersion,
    [Parameter(Mandatory = $true)][ValidatePattern('^v?\d+\.\d+\.\d+(\.\d+)?$')][string]$ReleaseTag,
    [Parameter(Mandatory = $true)][long]$DownloadCount,
    [Parameter(Mandatory = $true)][long]$LastUpdate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
foreach ($path in @($ManifestPath, $PluginManifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Manifest file not found: $path" }
}
$metadata = Get-Content -LiteralPath $PluginManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @(Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json)
$index = -1
for ($i = 0; $i -lt $entries.Count; $i++) {
    if ($entries[$i].InternalName -eq $metadata.InternalName) { $index = $i; break }
}
$entry = [ordered]@{}
if ($index -ge 0) {
    foreach ($property in $entries[$index].PSObject.Properties) { $entry[$property.Name] = $property.Value }
}
$localOnly = @('WorkingPluginId','InstalledFromUrl','Testing','Disabled','ScheduledForDeletion','PluginDirectory','DownloadCount','LastUpdate','RepoUrl','DownloadLinkInstall','DownloadLinkUpdate','AssemblyVersion')
foreach ($property in $metadata.PSObject.Properties) {
    if ($property.Name -notin $localOnly) { $entry[$property.Name] = $property.Value }
}
$entry['RepoUrl'] = $RepositoryUrl
$entry['AssemblyVersion'] = $AssemblyVersion
$entry['DownloadLinkInstall'] = "$RepositoryUrl/releases/download/$ReleaseTag/latest.zip"
$entry['DownloadLinkUpdate'] = "$RepositoryUrl/releases/download/$ReleaseTag/latest.zip"
$entry['DownloadCount'] = $DownloadCount
$entry['LastUpdate'] = $LastUpdate
if ($index -ge 0) { $entries[$index] = [pscustomobject]$entry } else { $entries += [pscustomobject]$entry }
Set-Content -LiteralPath $ManifestPath -Encoding UTF8 -Value (ConvertTo-Json ([object[]]$entries) -Depth 20)
Write-Host "Updated manifest entry '$($entry.InternalName)' in $ManifestPath"
