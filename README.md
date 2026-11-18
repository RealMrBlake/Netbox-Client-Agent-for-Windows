# NetBox Client Agent for Windows

Inventories a Windows PC for NetBox and the
[netbox-inventory](https://github.com/ArnesSI/netbox-inventory) plugin. The
result is a single, self-contained `agent.exe`; the client does not need .NET
installed.

| | |
|---|---|
| **Type** | Command-line agent (.NET 8) |
| **Platform** | Windows (x64, ARM64) |
| **NetBox** | `4.2` or newer recommended |
| **Deployment** | Intune (Win32 app) or Group Policy (startup script), fully unattended |
| **Repository** | <https://git.mrblake.cc/MrBlake/Netbox-Client-Agent-for-Windows> |

## Modes

The agent runs in one of three modes, set by `mode` in `config.json`:

| Mode | What it does | Needs on the client |
|---|---|---|
| `netbox` (default) | Writes the PC directly to NetBox once a day | NetBox URL and API token |
| `listen` | Serves the inventory on a TCP port; the **NetBox Synchronizer** desktop app scans IP ranges and transfers the PCs to NetBox after a preview | Only a shared scan key – **no NetBox access** |
| `both` | Both of the above | Both |

The agent is invisible to users: it runs as SYSTEM in the background (session
0), shows no window and causes no prompts. The installer creates the firewall
rule before the listener starts, so Windows never asks users to allow network
access.

## Features

In mode `netbox`, the agent creates or updates after the first run:

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

In mode `listen`, the NetBox Synchronizer (page *Windows-Clients*, target
mode *NetBox-Agent*) scans IP ranges, lists every PC that answers and
transfers the selected ones with model, serial number, operating system,
interfaces, MAC and IP addresses – the same way as its agentless WinRM scan.
Disks, memory modules and the software list are shown in the app as well.

## Requirements

Building requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
or newer.

For mode `netbox`:

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

For mode `listen`: the NetBox Synchronizer desktop app and TCP access from the
admin PC to the clients on `listen_port` (default 8731).

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

The result is `dist\win-x64\agent.exe` (`-Runtime win-arm64` for ARM devices).

### 2. Create the configuration

Mode `netbox`:

```powershell
Copy-Item .\config.example.json .\dist\win-x64\config.json
notepad .\dist\win-x64\config.json
```

Mode `listen`: in the NetBox Synchronizer open *Windows-Clients*, choose
*NetBox-Agent (Netzwerk-Scan)*, create a key and click
*Agent-Konfiguration speichern …*. This writes a ready `config.json`
(see also `config.listen.example.json`).

See [Configuration](#configuration) for all options.

### 3. Test

Show what the agent collects (any mode):

```powershell
.\dist\win-x64\agent.exe --config .\dist\win-x64\config.json --print
```

Mode `netbox` – the dry run reads hardware and NetBox but deliberately writes
nothing:

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json `
  --dry-run
```

Expected output:

```text
Collected: CLIENT01, LENOVO 21MW, S/N ABC123
Dry run successful; nothing was written.
```

Mode `listen` – serve in the foreground (Ctrl+C stops) and scan this PC from
the NetBox Synchronizer:

```powershell
.\dist\win-x64\agent.exe --config .\dist\win-x64\config.json --serve
```

When started by hand like this, Windows Firewall may ask once whether
`agent.exe` may accept connections – this only happens in manual tests. An
installed agent has its firewall rule and never asks.

### 4. First real synchronisation (mode `netbox`)

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json
```

Expected output:

```text
Synchronized: device #123, asset #456
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
- restricts access to the API token and scan key to SYSTEM and local administrators,
- mode `netbox`/`both`: creates the daily scheduled task `NetBox Windows Agent`
  and starts the first synchronisation immediately,
- mode `listen`/`both`: adds the inbound firewall rule `NetBox Windows Agent`
  for `listen_port` (domain and private profile, limited to
  `allowed_networks`), then creates and starts the scheduled task
  `NetBox Windows Agent Listener` (runs as SYSTEM at startup, restarted after
  failures),
- removes tasks and the firewall rule that a changed mode no longer needs,
- can be run again at any time to update the agent or its configuration.

The installation state only depends on the installed executable, configuration
and scheduled tasks. A temporary NetBox or network error during the first
synchronisation does not undo the installation; the scheduled task retries the
synchronisation later.

## Uninstall

As administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\uninstall.ps1
```

This removes both scheduled tasks, the firewall rule and the install folder.

## Configuration

### Mode `netbox`

```json
{
  "mode": "netbox",
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

`mode` may be omitted; `netbox` is the default. `site` must be the slug of an
existing NetBox site, since NetBox requires a site for every device. `location`
is optional and may be removed or set to `null`. `region` narrows the site
lookup.

### Mode `listen`

```json
{
  "mode": "listen",
  "listen_port": 8731,
  "scan_key": "the key from the NetBox Synchronizer",
  "allowed_networks": ["10.0.0.0/24"],
  "inventory_cache_seconds": 300,
  "collect_software": true,
  "max_software_entries": 250
}
```

| Setting | Meaning |
|---|---|
| `listen_port` | TCP port of the agent (default 8731) |
| `scan_key` | Shared secret of all agents of one customer (at least 16 characters); only a NetBox Synchronizer with the same key can read the inventory |
| `allowed_networks` | Optional networks in CIDR notation that may connect, e.g. the admin network. Empty = any address. Used for the firewall rule and checked by the agent |
| `inventory_cache_seconds` | The inventory is collected on request and reused for this time |

For mode `both`, combine the settings of both modes.

### Security of the listen mode

- `GET /v1/info` answers without authentication, with the agent name and
  version only.
- `GET /v1/inventory` needs an HMAC-SHA256 signature (key derived from
  `scan_key`) over a random nonce and the current time. Requests older than five
  minutes and repeated nonces are rejected.
- The inventory is returned encrypted with AES-256-GCM (a separate key derived
  from `scan_key`), so serial numbers and software lists never cross the network
  in clear text – without needing TLS certificates.
- The clocks of client and admin PC may differ by at most five minutes (domain
  time synchronisation is enough).
- The agent only reads data; it accepts no commands.

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

Both distribution methods are fully unattended: the installer runs as SYSTEM,
needs no user interaction and no administrator on the client, and the agent
stays invisible to users.

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

Device restart behavior:
No specific action

Detection rule (rule type "File"):
Path: C:\Program Files\NetBox Windows Agent
File or folder: agent.exe
Detection method: String (version)
Operator: Greater than or equal to
Value: 0.2.1   (the version of agent.exe, see agent.exe --version)
Associated with a 32-bit app on 64-bit clients: No
```

Assign the app as **Required** to a device group so it installs without user
action. The version rule lets Intune update clients with an older agent. After
changes to `agent.exe`, `config.json` or the installation scripts, rebuild the
`.intunewin` package and upload it again under
**App > Properties > App package file** – a change in the local repository does
not update a package that was already uploaded. A changed `config.json` alone
(for example a new scan key) is not detected by the version rule; publish it as
a new app that supersedes the old one, or use Group Policy.

### Distribution with Group Policy

1. Create a folder on a share that the group **Domain Computers** can read, for
   example `\\fileserver\NetBoxAgent$`, and copy these files into it:

   ```text
   agent.exe
   config.json
   install.ps1
   uninstall.ps1
   gpo-startup.ps1
   ```

2. Create a GPO linked to the OU of the computers:
   **Computer Configuration > Policies > Windows Settings > Scripts
   (Startup/Shutdown) > Startup > PowerShell Scripts > Add** – script name
   `\\fileserver\NetBoxAgent$\gpo-startup.ps1`, no parameters.

3. Make sure the share is reachable at startup:
   **Computer Configuration > Policies > Administrative Templates > System >
   Logon > Always wait for the network at computer startup and logon** =
   *Enabled*.

At every startup the script compares `agent.exe` and `config.json` on the share
with the installed version. If nothing changed, it does nothing; if a file
changed or the agent is missing, it runs `install.ps1` from the share. To update
all computers, replace the files on the share. The script logs to
`C:\ProgramData\NetBox Windows Agent\gpo-startup.log`.

To remove the agent from all computers of the GPO, add the script parameter
`-Uninstall`.

> `config.json` on the share is readable by all domain computers. In mode
> `listen` it contains the scan key, in mode `netbox` the NetBox API token – use
> a token with only the permissions listed above.

### Troubleshooting

**`agent.exe is missing in the source directory`** — `SourceDirectory` must be
the folder that contains both `agent.exe` and `config.json`:

```powershell
-SourceDirectory .\dist\win-x64
```

**The NetBox Synchronizer does not find an agent** — check that the task
`NetBox Windows Agent Listener` is running, test the port from the admin PC with
`Test-NetConnection <client> -Port 8731`, check firewalls between the networks
and that the admin PC is inside `allowed_networks`. The firewall rule applies to
the *Domain* and *Private* network profiles, not to *Public*.

**"Agent-Schlüssel stimmt nicht" in the NetBox Synchronizer** (wrong key) — the
key in the app and `scan_key` on the client differ; distribute the
configuration again.

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

**`400 Bad Request`** — the full response after `ERROR:` names the field NetBox
rejected. Often the configured device role, site or location is missing.

## Development

```powershell
dotnet build
.\scripts\build.ps1 -Runtime win-x64
```

GitHub Actions builds x64 and ARM64 artefacts automatically. `config.json`,
`dist`, `bin`, `obj` and local build tools are not committed.
