# NetBox Client Agent for Windows

Inventories a Windows PC automatically in NetBox and in the
[netbox-inventory](https://github.com/ArnesSI/netbox-inventory) plugin. The
result is a single, self-contained `agent.exe`; the client does not need .NET
installed.

| | |
|---|---|
| **Type** | Command-line agent (.NET 8) |
| **Platform** | Windows (x64, ARM64) |
| **NetBox** | `4.2` or newer recommended |
| **Repository** | <https://git.mrblake.cc/MrBlake/Netbox-Client-Agent-for-Windows> |

## Features

After the first run, the agent creates or updates:

- a `DCIM > Device` for the Windows PC,
- a linked inventory asset,
- interfaces for detected network adapters,
- an assigned primary MAC address per interface,
- modules and module bays for CPU, memory modules and disks,
- a hardware and software report in the comments of the device and asset.

The agent recognises the PC by its BIOS serial number, so repeated runs update
the existing objects. No NetBox objects are ever deleted.

> NetBox 4.2 or newer is recommended, since MAC addresses are separate objects
> from 4.2 on. Modules are meant for replaceable hardware in NetBox; for PC
> inventory, CPU, memory and disks are deliberately mapped onto this model.

## Requirements

1. `netbox-inventory` is installed.
2. A device role with the slug `windows-client` exists.
3. The site and location named in the configuration exist, if used.
4. The API token has read, create and change permissions for:
   - DCIM devices, manufacturers and device types
   - DCIM interfaces and MAC addresses
   - DCIM module types, module bays and modules
   - the plugin's inventory assets

The agent deliberately does not create sites or device roles. NetBox requires a
site for every device; a location is optional. Manufacturers, device types and
detected hardware types are created as needed.

Building requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
or newer.

## Installation

### 1. Build the executable

In PowerShell:

```powershell
git clone https://git.mrblake.cc/MrBlake/Netbox-Client-Agent-for-Windows.git
Set-Location .\Netbox-Client-Agent-for-Windows

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\build.ps1 `
  -Runtime win-x64
```

The result is `dist\win-x64\agent.exe`.

### 2. Create the configuration

```powershell
Copy-Item .\config.example.json .\dist\win-x64\config.json
notepad .\dist\win-x64\config.json
```

See [Configuration](#configuration) for all options.

### 3. Test the connection

The dry run reads hardware and NetBox but deliberately writes nothing:

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json `
  --dry-run
```

Expected output (the agent's messages are in German):

```text
Erfasst: CLIENT01, LENOVO 21MW, S/N ABC123
Dry-Run erfolgreich; keine Änderungen geschrieben.
```

### 4. First real synchronisation

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json
```

Expected output:

```text
Synchronisiert: Device #123, Asset #456
```

### 5. Install permanently

Open PowerShell **as administrator**:

```powershell
Set-Location C:\path\to\Netbox-Client-Agent-for-Windows

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\install.ps1 `
  -SourceDirectory .\dist\win-x64
```

The script:

- copies the executable and configuration to `C:\Program Files\NetBox Windows Agent`,
- restricts access to the API token to SYSTEM and local administrators,
- creates the daily scheduled task `NetBox Windows Agent`,
- starts the first synchronisation immediately.

## Uninstall

As administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\uninstall.ps1
```

## Configuration

Minimal example:

```json
{
  "netbox_url": "https://netbox.example.com",
  "api_token": "YOUR_NETBOX_TOKEN",
  "region": "emea",
  "site": "berlin",
  "location": "office-1",
  "device_role": "windows-client",
  "verify_tls": true,
  "allow_insecure_tls": false,
  "timeout_seconds": 30,
  "collect_software": true,
  "max_software_entries": 250,
  "create_hardware_modules": true,
  "create_interfaces": true,
  "custom_fields": {}
}
```

`site` must be the slug of an existing NetBox site, since NetBox requires a site
for every device. `location` is optional and may be removed or set to `null`.
`region` narrows the site lookup.

### Self-signed certificate

If NetBox uses a self-signed certificate and Windows does not trust the issuing
CA:

```json
"allow_insecure_tls": true
```

This ignores certificate chain and name errors. In production it is safer to
install the internal CA on the client and keep `allow_insecure_tls` set to
`false`.

### Disabling optional features

```json
"collect_software": false,
"create_hardware_modules": false,
"create_interfaces": false
```

`max_software_entries` may be between 0 and 1000. The agent collects
machine-wide classic Windows software from the 32- and 64-bit registry.
Per-user Store/AppX packages are not reliably available when running as SYSTEM.

### Custom fields

Custom fields are not required by default. To use them, create them for
`DCIM > Device` in NetBox first. Supported mappings:

```json
"custom_fields": {
  "agent_version": "netbox_agent_version",
  "windows_version": "windows_version",
  "device_uuid": "device_uuid",
  "last_sync": "agent_last_sync"
}
```

## Usage

### Distribution with Intune

Package these four files together as a Win32 app (`.intunewin`):

```text
agent.exe
config.json
install.ps1
uninstall.ps1
```

Intune settings:

```text
Install command:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -SourceDirectory .

Uninstall command:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1

Install behavior:
System

Detection rule (rule type "File"):
Path: C:\Program Files\NetBox Windows Agent
File or folder: agent.exe
Detection method: File or folder exists
Associated with a 32-bit app on 64-bit clients: No
```

### Troubleshooting

**`agent.exe fehlt im Quellverzeichnis`** (agent.exe missing in source
directory) — `SourceDirectory` must be the folder that contains both
`agent.exe` and `config.json`:

```powershell
-SourceDirectory .\dist\win-x64
```

**`RemoteCertificateNameMismatch` or `RemoteCertificateChainErrors`** — set
`allow_insecure_tls` to `true` for testing. Permanently, install the correct
internal CA and use a certificate name that matches the URL.

**Custom field does not exist** — if no custom fields were created in NetBox:

```json
"custom_fields": {}
```

**`403 Forbidden`** — the API token's user lacks permissions for the object
type named in the error.

**`site: This field is required`** — NetBox requires a site for every device.
Enter the slug of an existing site in `config.json`:

```json
"site": "berlin",
"location": null
```

**Intune reports a failed installation although the files exist** — in a file
detection rule, the `Path` field must contain only the folder. `agent.exe`
belongs in the separate `File or folder` field.

**`400 Bad Request`** — the full response after `FEHLER:` (error) names the
field NetBox rejected. Often the configured device role, site or location is
missing.

## Development

```powershell
dotnet build
.\scripts\build.ps1 -Runtime win-x64
```

GitHub Actions builds x64 and ARM64 artefacts automatically. `config.json`,
`dist`, `bin`, `obj` and local build tools are not committed.
