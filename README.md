# NetBox Windows Client Agent

Ein schlanker Windows-Agent, der Hardwaredaten per WMI erfasst und einen NetBox-Device-Datensatz sowie das zugehörige Asset im Plugin [ArnesSI/netbox-inventory](https://github.com/ArnesSI/netbox-inventory) anlegt oder aktualisiert. Das Release ist eine einzelne, selbstenthaltende `agent.exe`; auf dem Zielsystem muss kein .NET installiert sein.

## Was synchronisiert wird

- Rechnername, Hersteller, Modell, BIOS-Seriennummer und Geräte-UUID
- Windows-Version und Architektur, CPU, RAM, Datenträger inklusive Seriennummern, MAC- und IP-Adressen
- Maschinenweit installierte Software aus der 32- und 64-Bit-Windows-Registry
- NetBox Site und Location aus der Konfiguration
- `dcim.Device` sowie das verknüpfte `netbox_inventory.Asset`
- Hersteller und Device Type werden bei Bedarf angelegt
- Wiederholbare Updates anhand der BIOS-Seriennummer statt doppelter Datensätze

Der Agent löscht keine NetBox-Objekte. Site, Location und Device Role müssen bereits existieren. Die Region dient zur eindeutigen Auswahl der Site.

## Voraussetzungen in NetBox

1. Das Plugin `netbox-inventory` ist installiert.
2. Eine Device Role mit dem konfigurierten Slug (Standard: `windows-client`) existiert.
3. Site/Location existieren, falls sie in `config.json` gesetzt sind.
4. Der API-Token darf Devices, Manufacturers, Device Types und Inventory Assets lesen, anlegen und ändern.

Optionale Custom Fields müssen für `dcim.Device` existieren. Die linke Seite in `custom_fields` ist der Agent-Schlüssel, die rechte Seite der NetBox-Custom-Field-Name. Nicht benötigte Einträge können entfernt werden.

## Konfiguration

`config.example.json` als `config.json` neben die EXE kopieren. Die URL enthält nur die NetBox-Basis-URL, nicht `/api`.

```json
{
  "netbox_url": "https://netbox.example.com",
  "api_token": "0123456789abcdef",
  "region": "emea",
  "site": "berlin",
  "location": "office-1",
  "device_role": "windows-client",
  "verify_tls": true,
  "allow_insecure_tls": false,
  "timeout_seconds": 30,
  "collect_software": true,
  "max_software_entries": 250,
  "tags": [],
  "custom_fields": {}
}
```

`site` und `location` sind optional. Für NetBox-Installationen mit selbstsigniertem Zertifikat kann `allow_insecure_tls` auf `true` gesetzt werden. Damit werden sowohl Fehler der Zertifikatskette als auch abweichende Zertifikatsnamen ignoriert; dies reduziert die Sicherheit und sollte nur in vertrauenswürdigen internen Netzen verwendet werden. `verify_tls: false` bleibt aus Kompatibilitätsgründen ebenfalls unterstützt.

`collect_software` aktiviert die Softwareinventarisierung. `max_software_entries` begrenzt die Anzahl der alphabetisch sortierten Einträge zwischen 0 und 1000. Erfasst wird maschinenweit installierte klassische Windows-Software; benutzerspezifische Store-/AppX-Pakete sind im SYSTEM-Kontext nicht zuverlässig verfügbar.

## Ausführen und bauen

```powershell
.\agent.exe --dry-run
.\agent.exe
.\agent.exe --config C:\ProgramData\NetBoxAgent\config.json
```

Der Dry-Run liest NetBox und prüft die Zuordnungen, schreibt aber nichts. Zum lokalen Build wird das .NET 8 SDK benötigt:

```powershell
.\scripts\build.ps1 -Runtime win-x64
```

Das Ergebnis liegt in `dist\win-x64\agent.exe`. GitHub Actions erzeugt zusätzlich ARM64-Artefakte.

## Manuelle Installation und Intune

Für eine manuelle Installation `agent.exe`, `config.json`, `install.ps1` und `uninstall.ps1` in dasselbe Paketverzeichnis legen und PowerShell als Administrator starten:

```powershell
.\install.ps1 -SourceDirectory $PWD
```

Die Installation kopiert die Dateien nach `%ProgramFiles%\NetBox Windows Agent`, schränkt die ACL auf Administratoren/SYSTEM ein, erstellt die tägliche geplante Aufgabe `NetBox Windows Agent` und führt die erste Synchronisation aus.

Für Intune die vier Dateien mit dem Microsoft Win32 Content Prep Tool als `.intunewin` paketieren:

- Installationsbefehl: `powershell.exe -ExecutionPolicy Bypass -File .\install.ps1 -SourceDirectory .`
- Deinstallationsbefehl: `powershell.exe -ExecutionPolicy Bypass -File .\uninstall.ps1`
- Installationskontext: System
- Erkennungsregel: Datei `%ProgramFiles%\NetBox Windows Agent\agent.exe` vorhanden
- Rückgabecode `0` bedeutet erfolgreiche erste Synchronisation

## Sicherheit und Betrieb

- Einen eigenen NetBox-Token mit minimal nötigen Rechten verwenden.
- `verify_tls` in Produktion immer aktiviert lassen.
- Für größere Umgebungen den Token künftig besser über Windows Credential Manager oder ein Intune-verwaltetes Maschinenzertifikat bereitstellen.
- Logs können in Intune über die Ausgabe der geplanten Aufgabe ergänzt werden; eine Event-Log-Integration ist ein sinnvoller nächster Ausbau.

## Vorgeschlagene Commits

```text
feat: add Windows hardware inventory collector and NetBox sync
feat: add self-contained Windows build and Intune deployment scripts
docs: document configuration, permissions, and deployment
ci: build x64 and arm64 agent artifacts
```
