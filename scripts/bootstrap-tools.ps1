[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot '.tools'
$dotnetRoot = Join-Path $toolsRoot 'dotnet'
$dotnetExe = Join-Path $dotnetRoot 'dotnet.exe'
$innoRoot = Join-Path $toolsRoot 'inno'
$innoCompiler = Join-Path $innoRoot 'ISCC.exe'
$cacheRoot = Join-Path $toolsRoot 'cache'

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    $dotnetInstall = Join-Path $cacheRoot 'dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $dotnetInstall
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dotnetInstall `
        -Version '8.0.424' `
        -Architecture 'x64' `
        -InstallDir $dotnetRoot `
        -NoPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $innoCompiler)) {
    $innoInstaller = Join-Path $cacheRoot 'innosetup-6.7.3.exe'
    $innoUri = 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe'
    $expectedHash = '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732'
    Invoke-WebRequest -Uri $innoUri -OutFile $innoInstaller

    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $innoInstaller).Hash
    if ($actualHash -ne $expectedHash) {
        throw "Inno Setup SHA-256 mismatch. Expected $expectedHash, got $actualHash."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $innoInstaller
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
        throw "Inno Setup Authenticode signature is not valid for Pyrsys B.V. Status: $($signature.Status)."
    }

    New-Item -ItemType Directory -Force -Path $innoRoot | Out-Null
    $process = Start-Process `
        -FilePath $innoInstaller `
        -ArgumentList @('/PORTABLE=1', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=$innoRoot") `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup portable installation failed with exit code $($process.ExitCode)."
    }
}

if (-not (Test-Path -LiteralPath $dotnetExe)) {
    throw "Local .NET SDK was not found at $dotnetExe."
}
if (-not (Test-Path -LiteralPath $innoCompiler)) {
    throw "Local Inno Setup compiler was not found at $innoCompiler."
}

[pscustomobject]@{
    Dotnet = $dotnetExe
    InnoSetupCompiler = $innoCompiler
}
