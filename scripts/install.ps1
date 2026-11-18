param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent",
    [string]$Schedule = 'DAILY',
    [string]$StartTime = '09:00'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $SourceDirectory 'agent.exe'))) { throw 'agent.exe fehlt im Quellverzeichnis.' }
if (-not (Test-Path (Join-Path $SourceDirectory 'config.json'))) { throw 'config.json fehlt im Quellverzeichnis.' }
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-Item (Join-Path $SourceDirectory 'agent.exe') (Join-Path $InstallDirectory 'agent.exe') -Force
Copy-Item (Join-Path $SourceDirectory 'config.json') (Join-Path $InstallDirectory 'config.json') -Force
$acl = Get-Acl $InstallDirectory
$acl.SetAccessRuleProtection($true, $false)
$administratorsSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
$systemSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
$admins = New-Object System.Security.AccessControl.FileSystemAccessRule($administratorsSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$system = New-Object System.Security.AccessControl.FileSystemAccessRule($systemSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($admins); $acl.SetAccessRule($system); Set-Acl $InstallDirectory $acl
$agentPath = Join-Path $InstallDirectory 'agent.exe'
$configPath = Join-Path $InstallDirectory 'config.json'
if ($Schedule -ne 'DAILY') { throw "Derzeit wird nur Schedule 'DAILY' unterstützt." }
$action = New-ScheduledTaskAction -Execute $agentPath -Argument "--config `"$configPath`""
$trigger = New-ScheduledTaskTrigger -Daily -At $StartTime
$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
Register-ScheduledTask -TaskName 'NetBox Windows Agent' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
& $agentPath --config $configPath
$syncExitCode = $LASTEXITCODE
if ($syncExitCode -ne 0) {
    Write-Warning "Der Agent wurde installiert, aber die erste NetBox-Synchronisation ist mit Exitcode $syncExitCode fehlgeschlagen. Die geplante Aufgabe versucht es erneut."
} else {
    Write-Host 'Die erste NetBox-Synchronisation war erfolgreich.'
}
if (-not (Test-Path -LiteralPath $agentPath)) { throw 'Installation fehlgeschlagen: agent.exe wurde nicht installiert.' }
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Installation fehlgeschlagen: config.json wurde nicht installiert.' }
if (-not (Get-ScheduledTask -TaskName 'NetBox Windows Agent' -ErrorAction SilentlyContinue)) { throw 'Installation fehlgeschlagen: Die geplante Aufgabe fehlt.' }
Write-Host 'NetBox Windows Agent wurde erfolgreich installiert.'
exit 0
