param([string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent")
$ErrorActionPreference = 'Stop'
Unregister-ScheduledTask -TaskName 'NetBox Windows Agent' -Confirm:$false -ErrorAction SilentlyContinue
Stop-ScheduledTask -TaskName 'NetBox Windows Agent Listener' -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'NetBox Windows Agent Listener' -Confirm:$false -ErrorAction SilentlyContinue
Remove-NetFirewallRule -Name 'NetBoxWindowsAgent' -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $InstallDirectory) {
    $resolved = (Resolve-Path -LiteralPath $InstallDirectory).Path
    $programFiles = (Resolve-Path -LiteralPath $env:ProgramFiles).Path
    if (-not $resolved.StartsWith($programFiles + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe target: $resolved" }
    Get-Process -Name agent -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($resolved, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
