[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'GCC License Watchdog'
$sc = Join-Path $env:SystemRoot 'System32\sc.exe'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    return
}

if ($service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    (Get-Service -Name $serviceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

& $sc delete $serviceName | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to delete service '$serviceName'. sc.exe exit code: $LASTEXITCODE."
}

$deadline = [DateTime]::UtcNow.AddSeconds(30)
while ($null -ne (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
    if ([DateTime]::UtcNow -ge $deadline) {
        throw "Service '$serviceName' is still registered after 30 seconds."
    }
    Start-Sleep -Milliseconds 250
}
