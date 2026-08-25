[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Installer verification must run from an elevated process.'
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$watchdogServiceName = 'GCC License Watchdog'
$programDataDirectory = Join-Path $env:ProgramData $watchdogServiceName
$configPath = Join-Path $programDataDirectory 'appsettings.json'
$gccBefore = Get-CimInstance Win32_Process -Filter "Name='grdcontrol.exe'" |
    Select-Object -First 1 ProcessId, CreationDate
if ($null -eq $gccBefore) {
    throw 'grdcontrol.exe is not running before verification.'
}

function Invoke-Installer([string[]]$Arguments) {
    $process = Start-Process `
        -FilePath $installer `
        -ArgumentList $Arguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installer failed with exit code $($process.ExitCode)."
    }
}

Invoke-Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
$configHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash
Invoke-Installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-')
$configHashAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath $configPath).Hash

$programDataAcl = Get-Acl -LiteralPath $programDataDirectory
$usersSid = 'S-1-5-32-545'
$usersWriteRules = @($programDataAcl.Access | Where-Object {
    $_.AccessControlType -eq [System.Security.AccessControl.AccessControlType]::Allow -and
    $_.IdentityReference.Translate([System.Security.Principal.SecurityIdentifier]).Value -eq $usersSid -and
    ($_.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::Write) -ne 0
})
if (-not $programDataAcl.AreAccessRulesProtected -or $usersWriteRules.Count -ne 0) {
    throw 'ProgramData ACL allows ordinary users to modify watchdog configuration.'
}

$services = @(Get-CimInstance Win32_Service -Filter "Name='$watchdogServiceName'")
if ($services.Count -ne 1 -or $services[0].State -ne 'Running') {
    throw 'Reinstallation did not leave exactly one running watchdog service.'
}
if ($configHashBefore -ne $configHashAfter) {
    throw 'Reinstallation overwrote the ProgramData configuration.'
}

$uninstallEntry = Get-ItemProperty `
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*', `
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' `
    -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -eq $watchdogServiceName } |
    Select-Object -First 1
if ($null -eq $uninstallEntry) {
    throw 'Windows uninstall entry was not found.'
}

$uninstaller = $uninstallEntry.UninstallString.Trim('"')
$uninstallProcess = Start-Process `
    -FilePath $uninstaller `
    -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($uninstallProcess.ExitCode -ne 0) {
    throw "Uninstaller failed with exit code $($uninstallProcess.ExitCode)."
}

if ($null -ne (Get-Service -Name $watchdogServiceName -ErrorAction SilentlyContinue)) {
    throw 'Watchdog service remains registered after uninstall.'
}
if (Test-Path -LiteralPath $uninstallEntry.InstallLocation) {
    throw 'Application directory remains after uninstall.'
}
if (-not (Test-Path -LiteralPath $programDataDirectory)) {
    throw 'Silent uninstall should preserve ProgramData but it was removed.'
}

$gccAfter = Get-CimInstance Win32_Process -Filter "Name='grdcontrol.exe'" |
    Select-Object -First 1 ProcessId, CreationDate
if ($null -eq $gccAfter -or $gccAfter.ProcessId -ne $gccBefore.ProcessId) {
    throw 'Guardant Control Center changed during installer verification.'
}

$result = [pscustomobject]@{
    InstallCycles = 2
    WatchdogServiceRemoved = $true
    ConfigurationPreservedAcrossUpgrade = $true
    ProgramDataAclHardened = $true
    ProgramDataPreservedAfterSilentUninstall = $true
    GuardantProcessIdBefore = $gccBefore.ProcessId
    GuardantProcessIdAfter = $gccAfter.ProcessId
}

if ($ResultPath) {
    $resultDirectory = Split-Path -Parent $ResultPath
    if ($resultDirectory) {
        New-Item -ItemType Directory -Force -Path $resultDirectory | Out-Null
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8
}

$result
