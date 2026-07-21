using System.Text.Json;
using NetBoxWindowsAgent;

return await MainAsync(args);

static async Task<int> MainAsync(string[] args)
{
    try
    {
        var configPath = ValueAfter(args, "--config") ?? Path.Combine(AppContext.BaseDirectory, "config.json");
        var dryRun = args.Contains("--dry-run");
        if (args.Contains("--help")) { Usage(); return 0; }
        if (!File.Exists(configPath)) throw new FileNotFoundException("Konfigurationsdatei nicht gefunden.", configPath);
        var config = JsonSerializer.Deserialize<AgentConfig>(await File.ReadAllTextAsync(configPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Konfiguration ist leer.");
        Validate(config);
        var hardware = HardwareCollector.Collect();
        Console.WriteLine($"Erfasst: {hardware.Hostname}, {hardware.Manufacturer} {hardware.Model}, S/N {hardware.Serial}");
        using var client = new NetBoxClient(config);
        var result = await new SyncService(client, config).Sync(hardware, dryRun);
        Console.WriteLine(dryRun ? "Dry-Run erfolgreich; keine Änderungen geschrieben." : $"Synchronisiert: Device #{result.DeviceId}, Asset #{result.AssetId}");
        return 0;
    }
    catch (Exception ex)
    {
        var messages = new List<string>();
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (!messages.Contains(current.Message)) messages.Add(current.Message);
        Console.Error.WriteLine($"FEHLER: {string.Join(" -> ", messages)}");
        return 1;
    }
}
static string? ValueAfter(string[] args, string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
static void Validate(AgentConfig c)
{
    if (!Uri.TryCreate(c.NetBoxUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new InvalidOperationException("netbox_url ist ungültig.");
    if (string.IsNullOrWhiteSpace(c.ApiToken) || c.ApiToken == "CHANGE_ME") throw new InvalidOperationException("api_token fehlt.");
    if (string.IsNullOrWhiteSpace(c.DeviceRole)) throw new InvalidOperationException("device_role fehlt.");
}
static void Usage() => Console.WriteLine("agent.exe [--config PFAD] [--dry-run] [--help]");
