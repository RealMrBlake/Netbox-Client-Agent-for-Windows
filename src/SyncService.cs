namespace NetBoxWindowsAgent;

internal sealed class SyncService(NetBoxClient client, AgentConfig config)
{
    public async Task<(int DeviceId, int AssetId)> Sync(HardwareInfo hw, bool dryRun)
    {
        var site = await ResolveSite();
        var location = await ResolveLocation(site);
        var manufacturer = await EnsureManufacturer(hw.Manufacturer, dryRun);
        var deviceType = await EnsureDeviceType(manufacturer, hw.Model, dryRun);
        var role = await client.FindOne("dcim/device-roles", ("slug", config.DeviceRole))
            ?? throw new InvalidOperationException($"Geräterolle '{config.DeviceRole}' fehlt in NetBox.");

        var device = await client.FindOne("dcim/devices", ("serial", hw.Serial));
        var customFields = BuildCustomFields(hw);
        var inventoryReport = HardwareSummary(hw);
        var devicePayload = new Dictionary<string, object?> {
            ["name"] = hw.Hostname, ["device_type"] = deviceType.Id, ["role"] = role.Id,
            ["site"] = site?.Id, ["location"] = location?.Id, ["status"] = "active",
            ["serial"] = hw.Serial, ["description"] = $"{hw.WindowsCaption} ({hw.Architecture})",
            ["comments"] = inventoryReport, ["custom_fields"] = customFields
        };
        RemoveNulls(devicePayload);
        if (device is null) {
            if (dryRun) return (0, 0);
            device = await client.Create("dcim/devices", devicePayload);
        } else if (!dryRun) device = await client.Patch("dcim/devices", device.Id, devicePayload);

        if (!dryRun && config.CreateInterfaces) await SyncInterfaces(device.Id, hw.Networks);
        if (!dryRun && config.CreateHardwareModules) await SyncHardwareModules(device.Id, hw);

        var asset = await client.FindOne("plugins/inventory/assets", ("serial", hw.Serial));
        var assetPayload = new Dictionary<string, object?> {
            ["name"] = hw.Hostname, ["serial"] = hw.Serial, ["status"] = "used",
            ["device_type"] = deviceType.Id, ["device"] = device.Id,
            ["description"] = $"Windows-Client; UUID {hw.Uuid}", ["comments"] = inventoryReport
        };
        if (asset is null) {
            if (dryRun) return (device.Id, 0);
            asset = await client.Create("plugins/inventory/assets", assetPayload);
        } else if (!dryRun) asset = await client.Patch("plugins/inventory/assets", asset.Id, assetPayload);
        return (device.Id, asset.Id);
    }

    private async Task SyncInterfaces(int deviceId, IReadOnlyList<NetworkInfo> networks)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var network in networks.Where(n => !string.IsNullOrWhiteSpace(n.MacAddress)))
        {
            var baseName = network.Name.Length > 60 ? network.Name[..60] : network.Name;
            var name = baseName;
            for (var suffix = 2; !usedNames.Add(name); suffix++) name = $"{baseName[..Math.Min(baseName.Length, 56)]} {suffix}";
            var iface = await client.FindOne("dcim/interfaces", ("device_id", deviceId.ToString()), ("name", name));
            var payload = new { device = deviceId, name, type = "other", enabled = true,
                description = network.IpAddresses.Count == 0 ? "Windows Agent" : $"Windows Agent; IP: {string.Join(", ", network.IpAddresses)}" };
            iface = iface is null ? await client.Create("dcim/interfaces", payload) : await client.Patch("dcim/interfaces", iface.Id, payload);

            var mac = await client.FindOne("dcim/mac-addresses", ("mac_address", network.MacAddress));
            var macPayload = new { mac_address = network.MacAddress, assigned_object_type = "dcim.interface", assigned_object_id = iface.Id };
            mac = mac is null ? await client.Create("dcim/mac-addresses", macPayload) : await client.Patch("dcim/mac-addresses", mac.Id, macPayload);
            await client.Patch("dcim/interfaces", iface.Id, new { primary_mac_address = mac.Id });
        }
    }

    private async Task SyncHardwareModules(int deviceId, HardwareInfo hw)
    {
        var genericManufacturer = await EnsureManufacturer("Agent-discovered", false);
        var components = new List<(string Bay, string Model, string Serial)> { ("CPU 1", hw.Processor, "") };
        components.AddRange(hw.MemoryModules.Select((m, i) =>
            ($"RAM {CleanName(string.IsNullOrWhiteSpace(m.Bank) ? (i + 1).ToString() : m.Bank)}",
             $"RAM {m.CapacityBytes / 1073741824d:F0} GiB {m.PartNumber}".Trim(), m.Serial)));
        components.AddRange(hw.Disks.Select((d, i) => ($"Disk {i + 1}", $"{d.Model} {d.SizeBytes / 1000000000d:F0} GB", d.Serial)));

        foreach (var component in components.Where(c => !string.IsNullOrWhiteSpace(c.Model)))
        {
            var typeSlug = Slug(component.Model);
            var moduleType = await client.FindOne("dcim/module-types", ("manufacturer_id", genericManufacturer.Id.ToString()), ("model", component.Model));
            moduleType ??= await client.Create("dcim/module-types", new { manufacturer = genericManufacturer.Id, model = component.Model, slug = typeSlug });
            var bay = await client.FindOne("dcim/module-bays", ("device_id", deviceId.ToString()), ("name", component.Bay));
            bay ??= await client.Create("dcim/module-bays", new { device = deviceId, name = component.Bay, enabled = true });
            var module = await client.FindOne("dcim/modules", ("device_id", deviceId.ToString()), ("module_bay_id", bay.Id.ToString()));
            var modulePayload = new { device = deviceId, module_bay = bay.Id, module_type = moduleType.Id, status = "active", serial = component.Serial };
            if (module is null) await client.Create("dcim/modules", modulePayload); else await client.Patch("dcim/modules", module.Id, modulePayload);
        }
    }

    private async Task<NetBoxObject?> ResolveSite()
    {
        if (string.IsNullOrWhiteSpace(config.Site)) return null;
        var filters = new List<(string, string?)> { ("slug", config.Site) };
        if (!string.IsNullOrWhiteSpace(config.Region)) filters.Add(("region", config.Region));
        return await client.FindOne("dcim/sites", filters.ToArray())
            ?? throw new InvalidOperationException($"Standort '{config.Site}' (Region '{config.Region}') fehlt in NetBox.");
    }
    private async Task<NetBoxObject?> ResolveLocation(NetBoxObject? site)
    {
        if (string.IsNullOrWhiteSpace(config.Location)) return null;
        return await client.FindOne("dcim/locations", ("slug", config.Location), ("site_id", site?.Id.ToString()))
            ?? throw new InvalidOperationException($"Lokation '{config.Location}' fehlt in NetBox.");
    }
    private async Task<NetBoxObject> EnsureManufacturer(string name, bool dryRun)
    {
        var slug = Slug(name);
        var item = await client.FindOne("dcim/manufacturers", ("slug", slug));
        if (item is not null) return item;
        if (dryRun) return new NetBoxObject { Id = 0, Name = name, Slug = slug };
        return await client.Create("dcim/manufacturers", new { name, slug });
    }
    private async Task<NetBoxObject> EnsureDeviceType(NetBoxObject manufacturer, string model, bool dryRun)
    {
        var slug = Slug(model);
        var item = await client.FindOne("dcim/device-types", ("manufacturer_id", manufacturer.Id.ToString()), ("slug", slug));
        if (item is not null) return item;
        if (dryRun) return new NetBoxObject { Id = 0, Name = model, Slug = slug };
        return await client.Create("dcim/device-types", new { manufacturer = manufacturer.Id, model, slug });
    }
    private Dictionary<string, object> BuildCustomFields(HardwareInfo hw)
    {
        var available = new Dictionary<string, object> {
            ["agent_version"] = "0.1.0", ["windows_version"] = hw.WindowsVersion,
            ["device_uuid"] = hw.Uuid, ["last_sync"] = DateTimeOffset.UtcNow.ToString("O")
        };
        return config.CustomFields.Where(x => available.ContainsKey(x.Key)).ToDictionary(x => x.Value, x => available[x.Key]);
    }
    private static string HardwareSummary(HardwareInfo hw)
    {
        var lines = new List<string> {
            "Automatisch durch NetBox Windows Agent aktualisiert.", "", "## Hardware",
            $"- System: {hw.Manufacturer} {hw.Model}", $"- Seriennummer: {hw.Serial}", $"- UUID: {hw.Uuid}",
            $"- Betriebssystem: {hw.WindowsCaption} {hw.WindowsVersion} ({hw.Architecture})",
            $"- CPU: {hw.Processor}", $"- RAM: {hw.MemoryBytes / 1073741824d:F1} GiB", "", "### Datenträger"
        };
        lines.AddRange(hw.Disks.Select(d => $"- {d.Model} — {d.SizeBytes / 1000000000d:F0} GB — S/N {d.Serial} — {d.DeviceId}"));
        lines.AddRange(["", "### Netzwerk"]);
        lines.AddRange(hw.Networks.Select(n => $"- {n.Name} — {n.MacAddress} — {string.Join(", ", n.IpAddresses)}"));
        if (hw.Software.Count > 0)
        {
            lines.AddRange(["", $"## Installierte Software ({hw.Software.Count})"]);
            lines.AddRange(hw.Software.Select(s => $"- {s.Name}{(string.IsNullOrWhiteSpace(s.Version) ? "" : $" {s.Version}")}{(string.IsNullOrWhiteSpace(s.Publisher) ? "" : $" — {s.Publisher}")}"));
        }
        return string.Join('\n', lines);
    }
    private static string Slug(string value) => string.Concat(value.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
    private static string CleanName(string value) => new(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
    private static void RemoveNulls(Dictionary<string, object?> data) { foreach (var key in data.Where(x => x.Value is null).Select(x => x.Key).ToList()) data.Remove(key); }
}
