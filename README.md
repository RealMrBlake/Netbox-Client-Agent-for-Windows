# NetBox Client Agent für Windows

Der Agent inventarisiert einen Windows-PC automatisch in NetBox und im Plugin [ArnesSI/netbox-inventory](https://github.com/ArnesSI/netbox-inventory). Das Ergebnis ist eine einzelne, selbstenthaltende `agent.exe`; auf dem Client muss kein .NET installiert sein.

## Ergebnis in NetBox

Nach dem ersten Lauf entstehen beziehungsweise aktualisieren sich:

- ein `DCIM > Device` für den Windows-PC,
- ein damit verknüpftes Inventory-Asset,
- Interfaces für erkannte Netzwerkadapter,
- je Interface eine zugewiesene primäre MAC-Adresse,
- Module und Module Bays für CPU, RAM-Riegel und Datenträger,
- ein Hardware- und Softwarebericht in den Kommentaren von Device und Asset.

Der Agent erkennt den PC anhand seiner BIOS-Seriennummer wieder. Wiederholte Ausführungen aktualisieren deshalb die vorhandenen Objekte. Es werden keine NetBox-Objekte gelöscht.

> NetBox 4.2 oder neuer wird empfohlen, da MAC-Adressen seit 4.2 eigenständige Objekte sind. Module sind in NetBox eigentlich für austauschbare Hardwarekomponenten vorgesehen; für PC-Inventarisierung werden CPU, RAM und Datenträger bewusst auf dieses Modell abgebildet.

## 1. NetBox vorbereiten

Erforderlich:

1. `netbox-inventory` ist installiert.
2. Eine Device Role mit dem Slug `windows-client` existiert.
3. Die in der Konfiguration genannten Site und Location existieren, sofern sie verwendet werden.
4. Der API-Token besitzt Lese-, Erstell- und Änderungsrechte für:
   - DCIM Devices, Manufacturers und Device Types
   - DCIM Interfaces und MAC Addresses
   - DCIM Module Types, Module Bays und Modules
   - Inventory Assets des Plugins

Site und Device Role legt der Agent absichtlich nicht automatisch an. Eine Site ist für jedes NetBox Device zwingend erforderlich. Location ist optional. Hersteller, Device Types und erkannte Hardwaretypen legt der Agent bei Bedarf an.

## 2. Repository herunterladen und EXE bauen

PowerShell öffnen und ausführen:

```powershell
git clone https://git.mrblake.cc/MrBlake/Netbox-Client-Agent-for-Windows.git
Set-Location .\Netbox-Client-Agent-for-Windows

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\build.ps1 `
  -Runtime win-x64
```

Zum Bauen wird das [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) oder neuer benötigt. Die fertige Datei liegt anschließend hier:

```text
dist\win-x64\agent.exe
```

## 3. Konfiguration erstellen

```powershell
Copy-Item .\config.example.json .\dist\win-x64\config.json
notepad .\dist\win-x64\config.json
```

Minimalbeispiel zum Kopieren:

```json
{
  "netbox_url": "https://netbox.example.local",
  "api_token": "HIER_DEN_NETBOX_TOKEN_EINTRAGEN",
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

`site` muss den Slug einer vorhandenen NetBox Site enthalten, da NetBox für jedes DCIM Device eine Site verlangt. `location` ist optional und darf entfernt oder auf `null` gesetzt werden. `region` grenzt die Suche nach der Site ein.

### Selbstsigniertes Zertifikat

Wenn NetBox ein selbstsigniertes Zertifikat verwendet und Windows der ausstellenden CA nicht vertraut:

```json
"allow_insecure_tls": true
```

Dies ignoriert Zertifikatsketten- und Namensfehler. In produktiven Umgebungen ist es sicherer, die interne CA auf dem Client zu installieren und `allow_insecure_tls` auf `false` zu lassen.

### Optionale Funktionen abschalten

```json
"collect_software": false,
"create_hardware_modules": false,
"create_interfaces": false
```

`max_software_entries` darf zwischen 0 und 1000 liegen. Erfasst wird maschinenweit installierte klassische Windows-Software aus der 32- und 64-Bit-Registry. Benutzerspezifische Store-/AppX-Pakete sind beim Lauf als SYSTEM nicht zuverlässig verfügbar.

Custom Fields werden standardmäßig nicht benötigt. Sollen sie verwendet werden, müssen sie vorher für `DCIM > Device` in NetBox existieren. Unterstützte Zuordnungen sind:

```json
"custom_fields": {
  "agent_version": "netbox_agent_version",
  "windows_version": "windows_version",
  "device_uuid": "device_uuid",
  "last_sync": "agent_last_sync"
}
```

## 4. Verbindung testen

Der Dry-Run liest Hardware und NetBox, schreibt aber absichtlich nichts:

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json `
  --dry-run
```

Erwartete Ausgabe:

```text
Erfasst: CLIENT01, LENOVO 21MW, S/N ABC123
Dry-Run erfolgreich; keine Änderungen geschrieben.
```

## 5. Erste echte Synchronisation

```powershell
.\dist\win-x64\agent.exe `
  --config .\dist\win-x64\config.json
```

Erwartete Ausgabe:

```text
Synchronisiert: Device #123, Asset #456
```

## 6. Dauerhaft installieren

PowerShell **als Administrator** öffnen:

```powershell
Set-Location C:\Pfad\zum\Netbox-Client-Agent-for-Windows

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\install.ps1 `
  -SourceDirectory .\dist\win-x64
```

Das Skript:

- kopiert EXE und Konfiguration nach `C:\Program Files\NetBox Windows Agent`,
- erlaubt nur SYSTEM und lokalen Administratoren den Zugriff auf den API-Token,
- erstellt die tägliche geplante Aufgabe `NetBox Windows Agent`,
- startet sofort die erste Synchronisation.

Deinstallation, ebenfalls als Administrator:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\uninstall.ps1
```

## Intune-Verteilung

Diese vier Dateien gemeinsam als Win32-App (`.intunewin`) paketieren:

```text
agent.exe
config.json
install.ps1
uninstall.ps1
```

Copy-&-Paste-Werte für Intune:

```text
Installationsbefehl:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1 -SourceDirectory .

Deinstallationsbefehl:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1

Installationsverhalten:
System

Erkennungsregel (Regeltyp „Datei“):
Pfad: C:\Program Files\NetBox Windows Agent
Datei oder Ordner: agent.exe
Erkennungsmethode: Datei oder Ordner ist vorhanden
Auf 64-Bit-Clients einer 32-Bit-App zugeordnet: Nein
```

## Fehlerbehebung

### `agent.exe fehlt im Quellverzeichnis`

Als `SourceDirectory` muss der Ordner angegeben werden, in dem sowohl `agent.exe` als auch `config.json` liegen:

```powershell
-SourceDirectory .\dist\win-x64
```

### `RemoteCertificateNameMismatch` oder `RemoteCertificateChainErrors`

Zum Testen `allow_insecure_tls` auf `true` setzen. Dauerhaft besser die korrekte interne CA installieren und einen zur URL passenden Zertifikatsnamen verwenden.

### Custom Field ist nicht vorhanden

Wenn keine eigenen NetBox Custom Fields angelegt wurden:

```json
"custom_fields": {}
```

### `403 Forbidden`

Dem Benutzer des API-Tokens fehlen Rechte für den im Fehler genannten Objekttyp.

### `site: Dieses Feld ist erforderlich`

NetBox verlangt für jedes DCIM Device eine Site. In `config.json` den Slug einer vorhandenen Site eintragen:

```json
"site": "berlin",
"location": null
```

### Intune meldet Installation fehlgeschlagen, die Dateien sind aber vorhanden

Bei einer Datei-Erkennungsregel darf im Feld `Pfad` nur der Ordner stehen. `agent.exe` gehört ausschließlich in das separate Feld `Datei oder Ordner`:

```text
Pfad: C:\Program Files\NetBox Windows Agent
Datei oder Ordner: agent.exe
```

### `400 Bad Request`

Die vollständige Antwort hinter `FEHLER:` enthält das von NetBox beanstandete Feld. Häufig fehlen die konfigurierte Device Role, Site oder Location.

## Entwicklung

```powershell
dotnet build
.\scripts\build.ps1 -Runtime win-x64
```

GitHub Actions baut automatisch x64- und ARM64-Artefakte. `config.json`, `dist`, `bin`, `obj` und lokale Build-Werkzeuge werden nicht committed.
