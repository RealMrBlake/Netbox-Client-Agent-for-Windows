using System.Text.Json.Serialization;

namespace NetBoxWindowsAgent;

internal static class AgentInfo
{
    public const string Version = "0.2.0";
    public const int Protocol = 1;
}

internal sealed class AgentConfig
{
    // "netbox": write to NetBox directly (default), "listen": only serve the inventory to the
    // NetBox Synchronizer desktop app, "both": do both.
    [JsonPropertyName("mode")] public string Mode { get; set; } = "netbox";
    [JsonPropertyName("netbox_url")] public string NetBoxUrl { get; set; } = "";
    [JsonPropertyName("api_token")] public string ApiToken { get; set; } = "";
    [JsonPropertyName("region")] public string Region { get; set; } = "";
    [JsonPropertyName("site")] public string? Site { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("device_role")] public string DeviceRole { get; set; } = "windows-client";
    [JsonPropertyName("verify_tls")] public bool VerifyTls { get; set; } = true;
    [JsonPropertyName("allow_insecure_tls")] public bool AllowInsecureTls { get; set; } = false;
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; } = 30;
    [JsonPropertyName("collect_software")] public bool CollectSoftware { get; set; } = true;
    [JsonPropertyName("max_software_entries")] public int MaxSoftwareEntries { get; set; } = 250;
    [JsonPropertyName("create_hardware_modules")] public bool CreateHardwareModules { get; set; } = true;
    [JsonPropertyName("create_interfaces")] public bool CreateInterfaces { get; set; } = true;
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("custom_fields")] public Dictionary<string, string> CustomFields { get; set; } = new();
    // Listen mode
    [JsonPropertyName("listen_port")] public int ListenPort { get; set; } = 8731;
    [JsonPropertyName("scan_key")] public string ScanKey { get; set; } = "";
    [JsonPropertyName("allowed_networks")] public List<string> AllowedNetworks { get; set; } = [];
    [JsonPropertyName("inventory_cache_seconds")] public int InventoryCacheSeconds { get; set; } = 300;

    [JsonIgnore] public bool SyncsNetBox => Mode is "netbox" or "both";
    [JsonIgnore] public bool Listens => Mode is "listen" or "both";
}

internal sealed class HardwareInfo
{
    public string Hostname { get; init; } = "";
    public string Domain { get; init; } = "";
    public bool PartOfDomain { get; init; }
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductVersion { get; init; } = "";
    public string Serial { get; init; } = "";
    public string Uuid { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public IReadOnlyList<int> Chassis { get; init; } = [];
    public string User { get; init; } = "";
    public string WindowsCaption { get; init; } = "";
    public string WindowsVersion { get; init; } = "";
    public string WindowsBuild { get; init; } = "";
    public string WindowsDisplayVersion { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Installed { get; init; } = "";
    public string LastBoot { get; init; } = "";
    public string Processor { get; init; } = "";
    public int Cores { get; init; }
    public ulong MemoryBytes { get; init; }
    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];
    public IReadOnlyList<NetworkInfo> Networks { get; init; } = [];
    public IReadOnlyList<NicInfo> Nics { get; init; } = [];
    public IReadOnlyList<MemoryInfo> MemoryModules { get; init; } = [];
    public IReadOnlyList<SoftwareInfo> Software { get; init; } = [];
}

internal sealed record DiskInfo(
    [property: JsonPropertyName("device_id")] string DeviceId,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("size")] ulong SizeBytes);
internal sealed record NetworkInfo(string Name, string MacAddress, IReadOnlyList<string> IpAddresses);
internal sealed record MemoryInfo(
    [property: JsonPropertyName("bank")] string Bank,
    [property: JsonPropertyName("manufacturer")] string Manufacturer,
    [property: JsonPropertyName("part_number")] string PartNumber,
    [property: JsonPropertyName("serial")] string Serial,
    [property: JsonPropertyName("capacity")] ulong CapacityBytes);
internal sealed record SoftwareInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("publisher")] string Publisher);

// Same shape as the WinRM scan of the NetBox Synchronizer desktop app.
internal sealed record NicInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("mac")] string Mac,
    [property: JsonPropertyName("ips")] IReadOnlyList<string> Ips,
    [property: JsonPropertyName("masks")] IReadOnlyList<string> Masks,
    [property: JsonPropertyName("gateway")] IReadOnlyList<string> Gateway,
    [property: JsonPropertyName("dhcp")] bool Dhcp,
    [property: JsonPropertyName("physical")] bool Physical,
    [property: JsonPropertyName("speed")] ulong Speed);

internal sealed class Page<T>
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("results")] public List<T> Results { get; set; } = [];
}
internal sealed class NetBoxObject
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("slug")] public string? Slug { get; set; }
    [JsonPropertyName("serial")] public string? Serial { get; set; }
}
