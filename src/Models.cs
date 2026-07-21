using System.Text.Json.Serialization;

namespace NetBoxWindowsAgent;

internal sealed class AgentConfig
{
    [JsonPropertyName("netbox_url")] public string NetBoxUrl { get; set; } = "";
    [JsonPropertyName("api_token")] public string ApiToken { get; set; } = "";
    [JsonPropertyName("region")] public string Region { get; set; } = "";
    [JsonPropertyName("site")] public string? Site { get; set; }
    [JsonPropertyName("location")] public string? Location { get; set; }
    [JsonPropertyName("device_role")] public string DeviceRole { get; set; } = "windows-client";
    [JsonPropertyName("verify_tls")] public bool VerifyTls { get; set; } = true;
    [JsonPropertyName("timeout_seconds")] public int TimeoutSeconds { get; set; } = 30;
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("custom_fields")] public Dictionary<string, string> CustomFields { get; set; } = new();
}

internal sealed record HardwareInfo(
    string Hostname, string Manufacturer, string Model, string Serial, string Uuid,
    string WindowsCaption, string WindowsVersion, string Architecture, string Processor,
    ulong MemoryBytes, IReadOnlyList<DiskInfo> Disks, IReadOnlyList<NetworkInfo> Networks);
internal sealed record DiskInfo(string DeviceId, string Model, string Serial, ulong SizeBytes);
internal sealed record NetworkInfo(string Name, string MacAddress, IReadOnlyList<string> IpAddresses);

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

