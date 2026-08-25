[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$BinaryPath
)

$ErrorActionPreference = 'Stop'
$serviceName = 'GCC License Watchdog'
$sc = Join-Path $env:SystemRoot 'System32\sc.exe'
$programDataDirectory = Join-Path $env:ProgramData $serviceName

function Set-SecureProgramDataAcl([string]$Path) {
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    $acl = [System.Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
    $propagation = [System.Security.AccessControl.PropagationFlags]::None
    $allow = [System.Security.AccessControl.AccessControlType]::Allow
    $rules = @(
        @('S-1-5-18', [System.Security.AccessControl.FileSystemRights]::FullControl),
        @('S-1-5-32-544', [System.Security.AccessControl.FileSystemRights]::FullControl),
        @('S-1-5-32-545', [System.Security.AccessControl.FileSystemRights]::ReadAndExecute)
    )

    foreach ($rule in $rules) {
        $identity = [System.Security.Principal.SecurityIdentifier]::new($rule[0])
        $accessRule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            $rule[1],
            $inheritance,
            $propagation,
            $allow)
        $acl.AddAccessRule($accessRule)
    }

    $acl.SetOwner([System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Invoke-Sc([string[]]$Arguments) {
    & $sc @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Set-SecureProgramDataAcl $programDataDirectory

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

$quotedBinaryPath = '"' + $BinaryPath + '"'
if ($null -eq $service) {
    Invoke-Sc @(
        'create', $serviceName,
        'binPath=', $quotedBinaryPath,
        'start=', 'delayed-auto',
        'obj=', 'LocalSystem',
        'DisplayName=', $serviceName)
} else {
    Invoke-Sc @(
        'config', $serviceName,
        'binPath=', $quotedBinaryPath,
        'start=', 'delayed-auto',
        'obj=', 'LocalSystem',
        'DisplayName=', $serviceName)
}

Invoke-Sc @('description', $serviceName, 'Monitors local Guardant licenses and safely recovers Guardant Control Center.')
Invoke-Sc @('failure', $serviceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/30000/restart/60000')
Invoke-Sc @('failureflag', $serviceName, '1')

Start-Service -Name $serviceName -ErrorAction Stop
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
