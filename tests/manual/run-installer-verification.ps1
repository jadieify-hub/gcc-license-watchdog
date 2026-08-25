[CmdletBinding()]
param(
    [string]$InstallerPath = (Join-Path $PSScriptRoot '..\..\artifacts\installer\GCC-License-Watchdog-Setup.exe'),
    [string]$ResultPath = (Join-Path $PSScriptRoot '..\..\artifacts\installer-verification.json')
)

$ErrorActionPreference = 'Stop'
$verifierPath = Join-Path $PSScriptRoot 'verify-installer.ps1'
$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$verifier = (Resolve-Path -LiteralPath $verifierPath).Path
$result = [IO.Path]::GetFullPath($ResultPath)

$argumentLine = @(
    '-NoProfile'
    '-ExecutionPolicy Bypass'
    "-File `"$verifier`""
    "-InstallerPath `"$installer`""
    "-ResultPath `"$result`""
) -join ' '

$process = Start-Process `
    -FilePath 'powershell.exe' `
    -ArgumentList $argumentLine `
    -Verb RunAs `
    -WindowStyle Hidden `
    -Wait `
    -PassThru

if ($process.ExitCode -ne 0) {
    throw "Elevated installer verification failed with exit code $($process.ExitCode)."
}
if (-not (Test-Path -LiteralPath $result)) {
    throw 'Elevated verifier did not write its result.'
}

Get-Content -LiteralPath $result -Raw
