<#
.SYNOPSIS
    Group Policy computer startup script: installs or updates the NetBox Windows Agent from a share.

.DESCRIPTION
    Put agent.exe, config.json, install.ps1 and this script into one folder on a share that
    "Domain Computers" can read, e.g. \\fileserver\NetBoxAgent$ or \\domain\NETLOGON\NetBoxAgent,
    and add this script as a PowerShell startup script:
      Computer Configuration > Policies > Windows Settings > Scripts (Startup/Shutdown) > Startup
      > PowerShell Scripts > \\fileserver\NetBoxAgent$\gpo-startup.ps1
    It runs as SYSTEM at every startup. When agent.exe and config.json on the share are unchanged and
    the agent is installed, it does nothing. Otherwise it runs install.ps1 from the share.
    Set -Uninstall to remove the agent from all computers of the GPO.
    A log is written to %ProgramData%\NetBox Windows Agent\gpo-startup.log.
#>
param(
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent",
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:ProgramData 'NetBox Windows Agent'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$log = Join-Path $logDirectory 'gpo-startup.log'
function Write-Log([string]$Text) { Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) $Text" -Encoding UTF8 }

try {
    if ($Uninstall) {
        if (Test-Path -LiteralPath $InstallDirectory) {
            & (Join-Path $SourceDirectory 'uninstall.ps1') -InstallDirectory $InstallDirectory
            Write-Log 'Uninstalled.'
        }
        exit 0
    }
    $sourceAgent = Join-Path $SourceDirectory 'agent.exe'
    $sourceConfig = Join-Path $SourceDirectory 'config.json'
    if (-not (Test-Path -LiteralPath $sourceAgent) -or -not (Test-Path -LiteralPath $sourceConfig)) {
        throw "agent.exe or config.json not found in $SourceDirectory (share not reachable yet?)"
    }
    $wanted = @{
        agent = (Get-FileHash -LiteralPath $sourceAgent -Algorithm SHA256).Hash
        config = (Get-FileHash -LiteralPath $sourceConfig -Algorithm SHA256).Hash
    }
    $stateFile = Join-Path $InstallDirectory 'install-state.json'
    $current = if (Test-Path -LiteralPath $stateFile) { Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json } else { $null }
    $installed = (Test-Path -LiteralPath (Join-Path $InstallDirectory 'agent.exe')) -and $current -and
        $current.agent -eq $wanted.agent -and $current.config -eq $wanted.config
    $tasksPresent = (Get-ScheduledTask -TaskName 'NetBox Windows Agent*' -ErrorAction SilentlyContinue)
    if ($installed -and $tasksPresent) {
        # Up to date – make sure the listener runs (it normally starts by its own startup trigger).
        if ($current.mode -in 'listen', 'both') { Start-ScheduledTask -TaskName 'NetBox Windows Agent Listener' -ErrorAction SilentlyContinue }
        exit 0
    }
    Write-Log "Installing from $SourceDirectory"
    $output = & (Join-Path $SourceDirectory 'install.ps1') -SourceDirectory $SourceDirectory -InstallDirectory $InstallDirectory 2>&1
    $output | ForEach-Object { Write-Log "  $_" }
    Write-Log 'Done.'
    exit 0
} catch {
    Write-Log "ERROR: $($_.Exception.Message)"
    exit 1
}
