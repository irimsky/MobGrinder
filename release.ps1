[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidatePattern('^v?\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [switch]$Restore,
    [switch]$SkipRemoteTagCheck,
    [switch]$RequireMainBranch,
    [switch]$RequireCleanWorkspace
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$project = Join-Path $root 'MobGrinder\MobGrinder.csproj'
$package = Join-Path $root 'MobGrinder\bin\Release\MobGrinder\latest.zip'

function Normalize-Version([string]$value) {
    $normalized = $value.Trim() -replace '^[vV]', ''
    if ($normalized -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Invalid version format: $value" }
    $parts = @($normalized.Split('.') | ForEach-Object { [int]$_ })
    while ($parts.Count -lt 4) { $parts += 0 }
    return $parts -join '.'
}

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw "Project not found: $project" }
if ((git -C $root branch --show-current).Trim() -ne 'main') {
    if ($RequireMainBranch) { throw 'Release checks require the main branch.' }
    Write-Warning 'Current branch is not main; continuing because -RequireMainBranch was not specified.'
}
if ($RequireCleanWorkspace -and -not [string]::IsNullOrWhiteSpace((git -C $root status --porcelain))) {
    throw 'The working tree contains uncommitted changes.'
}

$projectText = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$match = [regex]::Match($projectText, '<Version>(?<version>[^<]+)</Version>')
if (-not $match.Success) { throw 'The project file does not contain Version.' }
if ((Normalize-Version $Version) -ne (Normalize-Version $match.Groups['version'].Value)) {
    throw "Release version $Version does not match project version $($match.Groups['version'].Value)."
}
if (-not $SkipRemoteTagCheck -and -not [string]::IsNullOrWhiteSpace((git -C $root remote))) {
    if (-not [string]::IsNullOrWhiteSpace((git -C $root ls-remote --tags origin "refs/tags/$Version"))) {
        throw "The remote tag already exists: $Version"
    }
}
if (-not $SkipRemoteTagCheck -and [string]::IsNullOrWhiteSpace((git -C $root remote))) {
    Write-Warning 'No remote is configured; skipping the remote tag check.'
}

$buildArgs = @{
    Configuration = 'Release'
    Restore = [bool]$Restore
    LockedMode = [bool]$Restore
}
& (Join-Path $root 'build.ps1') @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code: $LASTEXITCODE" }
& (Join-Path $root 'tools\Test-PluginPackage.ps1') -PackagePath $package
if ($LASTEXITCODE -ne 0) { throw "ZIP verification failed with exit code: $LASTEXITCODE" }
& (Join-Path $root 'tools\Generate-ReleaseMetadata.ps1') -PackagePath $package -OutputDirectory (Split-Path $package) -ReleaseVersion $Version -AssemblyVersion $match.Groups['version'].Value -Repository ((git -C $root remote get-url origin 2>$null)) -Commit ((git -C $root rev-parse HEAD).Trim())
Write-Host "Release checks completed: $package"
