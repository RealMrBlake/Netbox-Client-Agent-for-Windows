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
$admins = New-Object System.Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$system = New-Object System.Security.AccessControl.FileSystemAccessRule('NT AUTHORITY\SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($admins); $acl.SetAccessRule($system); Set-Acl $InstallDirectory $acl
$command = '"' + (Join-Path $InstallDirectory 'agent.exe') + '" --config "' + (Join-Path $InstallDirectory 'config.json') + '"'
schtasks.exe /Create /TN 'NetBox Windows Agent' /SC $Schedule /ST $StartTime /RU SYSTEM /RL HIGHEST /TR $command /F | Out-Null
& (Join-Path $InstallDirectory 'agent.exe') --config (Join-Path $InstallDirectory 'config.json')

