[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Version = '0.1.1'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$tools = & (Join-Path $PSScriptRoot 'bootstrap-tools.ps1')
$dotnet = $tools.Dotnet
$iscc = $tools.InnoSetupCompiler
$artifactsRoot = Join-Path $projectRoot 'artifacts'
$publishRoot = Join-Path $artifactsRoot 'publish\win-x86'
$installerRoot = Join-Path $artifactsRoot 'installer'

function Assert-SafeArtifactPath([string]$Path) {
    $resolvedRoot = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the project: $resolvedPath"
    }
}

foreach ($path in @($publishRoot, $installerRoot)) {
    Assert-SafeArtifactPath $path
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

& $dotnet restore (Join-Path $projectRoot 'GccLicenseWatchdog.sln')
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

& $dotnet test (Join-Path $projectRoot 'GccLicenseWatchdog.sln') `
    --configuration $Configuration `
    --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

& $dotnet restore (Join-Path $projectRoot 'src\GccLicenseWatchdog\GccLicenseWatchdog.csproj') `
    --runtime 'win-x86'
if ($LASTEXITCODE -ne 0) { throw "win-x86 restore failed with exit code $LASTEXITCODE." }

& $dotnet publish (Join-Path $projectRoot 'src\GccLicenseWatchdog\GccLicenseWatchdog.csproj') `
    --configuration $Configuration `
    --runtime 'win-x86' `
    --self-contained true `
    --no-restore `
    --output $publishRoot `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

& $iscc `
    "/DSourceDir=$publishRoot" `
    "/DOutputDir=$installerRoot" `
    "/DAppVersion=$Version" `
    (Join-Path $projectRoot 'installer\GccLicenseWatchdog.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }

$installer = Join-Path $installerRoot 'GCC-License-Watchdog-Setup.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Expected installer was not produced at $installer."
}

Get-Item -LiteralPath $installer
