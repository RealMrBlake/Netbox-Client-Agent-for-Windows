param([string]$InstallDirectory = "$env:ProgramFiles\NetBox Windows Agent")
$ErrorActionPreference = 'Stop'
schtasks.exe /Delete /TN 'NetBox Windows Agent' /F 2>$null
if (Test-Path -LiteralPath $InstallDirectory) {
    $resolved = (Resolve-Path -LiteralPath $InstallDirectory).Path
    $programFiles = (Resolve-Path -LiteralPath $env:ProgramFiles).Path
    if (-not $resolved.StartsWith($programFiles + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unsicheres Ziel: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

