param([string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent")
$ErrorActionPreference = 'Stop'
Unregister-ScheduledTask -TaskName 'NetBox Windows Agent' -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $InstallDirectory) {
    $resolved = (Resolve-Path -LiteralPath $InstallDirectory).Path
    $programFiles = (Resolve-Path -LiteralPath $env:ProgramFiles).Path
    if (-not $resolved.StartsWith($programFiles + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unsicheres Ziel: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
