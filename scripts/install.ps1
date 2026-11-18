<#
.SYNOPSIS
    Installs or updates the NetBox Windows Agent (Intune Win32 app, GPO startup script or manual).

.DESCRIPTION
    Copies agent.exe and config.json to the install directory, restricts access to SYSTEM and
    administrators and sets up what the configured mode needs:
      netbox  daily scheduled task "NetBox Windows Agent" that writes to NetBox
      listen  scheduled task "NetBox Windows Agent Listener" (at startup, restarted on failure) and an
              inbound firewall rule, so the NetBox Synchronizer desktop app can scan the inventory
      both    both of the above
    The script is idempotent: running it again updates the files and the tasks.
#>
param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent",
    [string]$Schedule = 'DAILY',
    [string]$StartTime = '09:00'
)
$ErrorActionPreference = 'Stop'
$syncTask = 'NetBox Windows Agent'
$listenerTask = 'NetBox Windows Agent Listener'
$firewallRule = 'NetBoxWindowsAgent'

$principalNow = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principalNow.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'install.ps1 must run as administrator or SYSTEM (PowerShell: "Run as administrator"; Intune: install behavior "System").'
}

$sourceAgent = Join-Path $SourceDirectory 'agent.exe'
$sourceConfig = Join-Path $SourceDirectory 'config.json'
if (-not (Test-Path -LiteralPath $sourceAgent)) { throw "agent.exe is missing in the source directory $SourceDirectory." }
if (-not (Test-Path -LiteralPath $sourceConfig)) { throw "config.json is missing in the source directory $SourceDirectory." }

# Copies a file, retrying while it is briefly locked (e.g. an antivirus scan of the new agent.exe).
function Copy-WithRetry([string]$Source, [string]$Destination) {
    for ($attempt = 1; ; $attempt++) {
        try { Copy-Item -LiteralPath $Source -Destination $Destination -Force; return }
        catch [System.UnauthorizedAccessException], [System.IO.IOException] {
            if ($attempt -ge 10) { throw "Cannot write $Destination ($($_.Exception.Message)). Is agent.exe still running or blocked by an antivirus?" }
            Start-Sleep -Seconds 2
        }
    }
}
if ($Schedule -ne 'DAILY') { throw "Only the schedule 'DAILY' is supported." }

$config = Get-Content -LiteralPath $sourceConfig -Raw | ConvertFrom-Json
$mode = if ($config.mode) { [string]$config.mode } else { 'netbox' }
if ($mode -notin 'netbox', 'listen', 'both') { throw "config.json: mode must be 'netbox', 'listen' or 'both'." }
$port = if ($config.listen_port) { [int]$config.listen_port } else { 8731 }
$allowed = @($config.allowed_networks | Where-Object { $_ })
$syncs = $mode -in 'netbox', 'both'
$listens = $mode -in 'listen', 'both'

# A running listener locks agent.exe – stop it before the files are replaced.
Stop-ScheduledTask -TaskName $listenerTask -ErrorAction SilentlyContinue
Get-Process -Name agent -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDirectory, [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
Copy-WithRetry $sourceAgent (Join-Path $InstallDirectory 'agent.exe')
Copy-WithRetry $sourceConfig (Join-Path $InstallDirectory 'config.json')
$acl = Get-Acl $InstallDirectory
$acl.SetAccessRuleProtection($true, $false)
$administratorsSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
$systemSid = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
$admins = New-Object System.Security.AccessControl.FileSystemAccessRule($administratorsSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$system = New-Object System.Security.AccessControl.FileSystemAccessRule($systemSid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($admins); $acl.SetAccessRule($system); Set-Acl $InstallDirectory $acl
$agentPath = Join-Path $InstallDirectory 'agent.exe'
$configPath = Join-Path $InstallDirectory 'config.json'
$principal = New-ScheduledTaskPrincipal -UserId 'S-1-5-18' -LogonType ServiceAccount -RunLevel Highest

# --- NetBox sync (mode netbox/both) -------------------------------------------------------------
if ($syncs) {
    $action = New-ScheduledTaskAction -Execute $agentPath -Argument "--config `"$configPath`""
    $trigger = New-ScheduledTaskTrigger -Daily -At $StartTime
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 15)
    Register-ScheduledTask -TaskName $syncTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
} else {
    Unregister-ScheduledTask -TaskName $syncTask -Confirm:$false -ErrorAction SilentlyContinue
}

# --- Listener for the NetBox Synchronizer (mode listen/both) ------------------------------------
Remove-NetFirewallRule -Name $firewallRule -ErrorAction SilentlyContinue
if ($listens) {
    $action = New-ScheduledTaskAction -Execute $agentPath -Argument "--config `"$configPath`" --serve"
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Seconds 0) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) `
        -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $listenerTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    $remote = if ($allowed.Count -gt 0) { $allowed } else { 'Any' }
    New-NetFirewallRule -Name $firewallRule -DisplayName 'NetBox Windows Agent' -Direction Inbound -Protocol TCP `
        -LocalPort $port -Program $agentPath -RemoteAddress $remote -Profile Domain, Private -Action Allow | Out-Null
    Start-ScheduledTask -TaskName $listenerTask
} else {
    Unregister-ScheduledTask -TaskName $listenerTask -Confirm:$false -ErrorAction SilentlyContinue
}

# --- First NetBox sync ----------------------------------------------------------------------------
if ($syncs) {
    & $agentPath --config $configPath
    $syncExitCode = $LASTEXITCODE
    if ($syncExitCode -ne 0) {
        Write-Warning "The agent is installed, but the first NetBox sync failed with exit code $syncExitCode. The scheduled task retries it."
    } else {
        Write-Host 'The first NetBox sync was successful.'
    }
}

# --- Checks (decide the Intune installation state) -----------------------------------------------
if (-not (Test-Path -LiteralPath $agentPath)) { throw 'Installation failed: agent.exe was not installed.' }
if (-not (Test-Path -LiteralPath $configPath)) { throw 'Installation failed: config.json was not installed.' }
if ($syncs -and -not (Get-ScheduledTask -TaskName $syncTask -ErrorAction SilentlyContinue)) { throw 'Installation failed: the scheduled task is missing.' }
if ($listens -and -not (Get-ScheduledTask -TaskName $listenerTask -ErrorAction SilentlyContinue)) { throw 'Installation failed: the listener task is missing.' }

# Fingerprint of the installed files, used by gpo-startup.ps1 to skip unchanged installations.
$state = [ordered]@{
    agent = (Get-FileHash -LiteralPath $agentPath -Algorithm SHA256).Hash
    config = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash
    mode = $mode
    installed = (Get-Date).ToString('o')
}
$state | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallDirectory 'install-state.json') -Encoding UTF8
Write-Host "NetBox Windows Agent was installed successfully (mode '$mode')."
exit 0
