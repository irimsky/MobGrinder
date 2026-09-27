[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Restore,
    [switch]$LockedMode,
    [string]$DotnetPath = 'dotnet',
    [string]$DalamudLibPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($LockedMode -and -not $Restore) {
    throw '-LockedMode requires -Restore.'
}

$projectFile = Join-Path $PSScriptRoot 'MobGrinder/MobGrinder.csproj'
$taskDotnetHome = Join-Path $PSScriptRoot '.dotnet-home'
$env:DOTNET_CLI_HOME = $taskDotnetHome
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.nuget'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$properties = @()
if (-not [string]::IsNullOrWhiteSpace($DalamudLibPath)) {
    $runtimePath = (Resolve-Path -LiteralPath $DalamudLibPath).Path
    if (-not (Test-Path -LiteralPath (Join-Path $runtimePath 'Dalamud.dll'))) {
        throw "Dalamud.dll was not found in the specified directory: $runtimePath"
    }
    $properties += "-p:DalamudLibPath=$runtimePath"
}

Push-Location -LiteralPath $PSScriptRoot
try {
    if ($Restore) {
        $restoreArguments = @('restore', $projectFile) + $properties
        if ($LockedMode) { $restoreArguments += '--locked-mode' }
        & $DotnetPath @restoreArguments
        if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code: $LASTEXITCODE" }
    }

    $buildArguments = @('build', $projectFile, '-c', $Configuration, '--no-restore') + $properties
    & $DotnetPath @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code: $LASTEXITCODE" }
}
finally {
    Pop-Location
}
