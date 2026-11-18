using System.Text.Json;
using NetBoxWindowsAgent;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    try
    {
        if (args.Contains("--help")) { Usage(); return 0; }
        if (args.Contains("--version")) { Console.WriteLine(AgentInfo.Version); return 0; }
        var configPath = ValueAfter(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(configPath)) throw new FileNotFoundException("Configuration file not found.", configPath);
        var config = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(configPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Configuration is empty.");
        Validate(config);
        HardwareInfo Collect() => HardwareCollector.Collect(config.CollectSoftware, config.MaxSoftwareEntries);

        if (args.Contains("--print"))
        {
            Console.WriteLine(JsonSerializer.Serialize(InventoryDocument.Build(Collect()), new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        if (args.Contains("--serve"))
        {
            if (!config.Listens) throw new InvalidOperationException("--serve requires \"mode\": \"listen\" or \"both\".");
            using var cancel = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
            await new InventoryServer(config, Collect).Run(cancel.Token);
            return 0;
        }
        if (!config.SyncsNetBox)
        {
            Console.WriteLine("Mode \"listen\": nothing to sync. The inventory is served by \"agent.exe --serve\".");
            return 0;
        }

        var dryRun = args.Contains("--dry-run");
        var hardware = Collect();
        Console.WriteLine($"Collected: {hardware.Hostname}, {hardware.Manufacturer} {hardware.Model}, S/N {hardware.Serial}");
        using var client = new NetBoxClient(config);
        var result = await new SyncService(client, config).Sync(hardware, dryRun);
        Console.WriteLine(dryRun ? "Dry run successful; nothing was written." : $"Synchronized: device #{result.DeviceId}, asset #{result.AssetId}");
        return 0;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (!messages.Contains(current.Message)) messages.Add(current.Message);
        Console.Error.WriteLine($"ERROR: {string.Join(" -> ", messages)}");
        return 1;
    }
    catch (OperationCanceledException)
    {
        return 0;
    }
}
static string? ValueAfter(string[] args, string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static void Validate(AgentConfig c)
{
    if (c.Mode is not ("netbox" or "listen" or "both")) throw new InvalidOperationException("mode must be \"netbox\", \"listen\" or \"both\".");
    if (c.MaxSoftwareEntries < 0 || c.MaxSoftwareEntries > 1000) throw new InvalidOperationException("max_software_entries must be between 0 and 1000.");
    if (c.SyncsNetBox)
    {
        if (!Uri.TryCreate(c.NetBoxUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new InvalidOperationException("netbox_url is invalid.");
        if (string.IsNullOrWhiteSpace(c.ApiToken) || c.ApiToken == "CHANGE_ME") throw new InvalidOperationException("api_token is missing.");
        if (string.IsNullOrWhiteSpace(c.DeviceRole)) throw new InvalidOperationException("device_role is missing.");
        if (string.IsNullOrWhiteSpace(c.Site)) throw new InvalidOperationException("site is missing. NetBox requires a site for every DCIM device; only location is optional.");
    }
    if (c.Listens)
    {
        if (c.ScanKey.Length < 16 || c.ScanKey == "CHANGE_ME") throw new InvalidOperationException("scan_key must have at least 16 characters.");
        if (c.ListenPort is < 1 or > 65535) throw new InvalidOperationException("listen_port is invalid.");
        foreach (var network in c.AllowedNetworks)
            if (!System.Net.IPNetwork.TryParse(network, out _)) throw new InvalidOperationException($"allowed_networks: \"{network}\" is not a network in CIDR notation.");
    }
}
static void Usage() => Console.WriteLine("""
    agent.exe [--config PATH] [--dry-run]   collect and write to NetBox (mode "netbox" or "both")
    agent.exe [--config PATH] --serve       serve the inventory to the NetBox Synchronizer (mode "listen" or "both")
    agent.exe [--config PATH] --print       print the collected inventory as JSON
    agent.exe --version | --help
    """);
