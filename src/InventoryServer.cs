using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetBoxWindowsAgent;

/// <summary>
/// Serves the inventory to the NetBox Synchronizer desktop app ("listen" mode).
///
/// Protocol v1 over plain HTTP:
///   GET /v1/info                         unauthenticated: agent name, version and protocol
///   GET /v1/inventory?nonce=N&amp;ts=T   header X-Agent-Signature: hex(HMAC-SHA256(authKey, "GET\n/v1/inventory\nN\nT"))
/// The inventory is returned encrypted with AES-256-GCM (encKey, random 12 byte IV, the request nonce as
/// associated data): {"protocol":1,"iv":base64,"data":base64(ciphertext||tag)}.
/// authKey = HMAC-SHA256(UTF8(scan_key), "netbox-agent auth v1"), encKey = HMAC-SHA256(UTF8(scan_key), "netbox-agent enc v1").
/// Requests older than 5 minutes and repeated nonces are rejected.
/// </summary>
internal sealed class InventoryServer(AgentConfig config, Func<HardwareInfo> collect)
{
    private static readonly Regex NonceFormat = new("^[0-9a-f]{32}$", RegexOptions.Compiled);
    private readonly byte[] _authKey = DeriveKey(config.ScanKey, "netbox-agent auth v1");
    private readonly byte[] _encKey = DeriveKey(config.ScanKey, "netbox-agent enc v1");
    private readonly ConcurrentDictionary<string, long> _nonces = new();
    private readonly List<IPNetwork> _allowed = config.AllowedNetworks.Select(IPNetwork.Parse).ToList();
    private readonly SemaphoreSlim _collectLock = new(1, 1);
    private byte[]? _cached;
    private DateTime _cachedAt;

    public static byte[] DeriveKey(string secret, string purpose) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(purpose));

    public async Task Run(CancellationToken cancel)
    {
        var listener = new TcpListener(IPAddress.IPv6Any, config.ListenPort);
        listener.Server.DualMode = true;
        listener.Start();
        Console.WriteLine($"Listening on TCP port {config.ListenPort} (protocol v{AgentInfo.Protocol}).");
        while (!cancel.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(cancel);
            _ = Task.Run(() => Handle(client), cancel);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = client.SendTimeout = 10000;
                var stream = client.GetStream();
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
                if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
                var (status, body) = await Respond(stream, remote);
                var bytes = Encoding.UTF8.GetBytes(body);
                var header = $"HTTP/1.1 {status}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n" +
                             "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
                await stream.WriteAsync(bytes);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // client went away
            }
        }
    }

    private async Task<(string Status, string Body)> Respond(NetworkStream stream, IPAddress remote)
    {
        if (_allowed.Count > 0 && !_allowed.Any(network => network.Contains(remote)))
            return ("403 Forbidden", Error("address not allowed"));
        var request = await ReadRequest(stream);
        if (request is null) return ("400 Bad Request", Error("bad request"));
        var (method, target, headers) = request.Value;
        if (method != "GET") return ("405 Method Not Allowed", Error("only GET"));
        var path = target.Split('?')[0];
        var query = ParseQuery(target);
        if (path == "/v1/info")
            return ("200 OK", JsonSerializer.Serialize(new { agent = "netbox-windows-agent", version = AgentInfo.Version,
                protocol = AgentInfo.Protocol }));
        if (path != "/v1/inventory") return ("404 Not Found", Error("not found"));

        var nonce = query.GetValueOrDefault("nonce", "");
        if (!NonceFormat.IsMatch(nonce) || !long.TryParse(query.GetValueOrDefault("ts", ""), out var ts))
            return ("400 Bad Request", Error("nonce or ts missing"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - ts) > 300) return ("401 Unauthorized", Error("clock skew too large"));
        var expected = HMACSHA256.HashData(_authKey, Encoding.UTF8.GetBytes($"GET\n/v1/inventory\n{nonce}\n{ts}"));
        byte[] given;
        try { given = Convert.FromHexString(headers.GetValueOrDefault("x-agent-signature", "")); }
        catch (FormatException) { given = []; }
        if (!CryptographicOperations.FixedTimeEquals(expected, given)) return ("401 Unauthorized", Error("wrong key"));
        foreach (var old in _nonces.Where(item => now - item.Value > 600).Select(item => item.Key).ToList())
            _nonces.TryRemove(old, out _);
        if (!_nonces.TryAdd(nonce, now)) return ("401 Unauthorized", Error("nonce reused"));

        var plain = await Inventory();
        var iv = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(_encKey, 16)) aes.Encrypt(iv, plain, cipher, tag, Encoding.ASCII.GetBytes(nonce));
        return ("200 OK", JsonSerializer.Serialize(new { protocol = AgentInfo.Protocol, iv = Convert.ToBase64String(iv),
            data = Convert.ToBase64String([.. cipher, .. tag]) }));
    }

    private async Task<byte[]> Inventory()
    {
        await _collectLock.WaitAsync();
        try
        {
            if (_cached is null || DateTime.UtcNow - _cachedAt > TimeSpan.FromSeconds(Math.Max(0, config.InventoryCacheSeconds)))
            {
                _cached = JsonSerializer.SerializeToUtf8Bytes(InventoryDocument.Build(collect()));
                _cachedAt = DateTime.UtcNow;
            }
            return _cached;
        }
        finally { _collectLock.Release(); }
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers)?> ReadRequest(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length));
            if (read == 0) break;
            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
        }
        var text = Encoding.ASCII.GetString(buffer, 0, length);
        var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end < 0) return null;
        var lines = text[..end].Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        return (first[0], first[1], headers);
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>();
        var index = target.IndexOf('?');
        if (index < 0) return result;
        foreach (var part in target[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            result[Uri.UnescapeDataString(pair[0])] = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : "";
        }
        return result;
    }

    private static string Error(string text) => JsonSerializer.Serialize(new { error = text });
}

/// <summary>Inventory in the shape of the WinRM scan of the desktop app, plus agent-only details.</summary>
internal static class InventoryDocument
{
    public static Dictionary<string, object?> Build(HardwareInfo hw) => new()
    {
        ["ok"] = true,
        ["source"] = "agent",
        ["agent_version"] = AgentInfo.Version,
        ["collected_at"] = DateTimeOffset.UtcNow.ToString("O"),
        ["name"] = hw.Hostname,
        ["domain"] = hw.Domain,
        ["part_of_domain"] = hw.PartOfDomain,
        ["manufacturer"] = hw.Manufacturer,
        ["model"] = hw.Model,
        ["product_name"] = hw.ProductName,
        ["product_version"] = hw.ProductVersion,
        ["serial"] = hw.Serial,
        ["uuid"] = hw.Uuid,
        ["bios_version"] = hw.BiosVersion,
        ["chassis"] = hw.Chassis,
        ["user"] = hw.User,
        ["memory"] = hw.MemoryBytes,
        ["cpu"] = hw.Processor,
        ["cores"] = hw.Cores,
        ["os"] = hw.WindowsCaption,
        ["os_version"] = hw.WindowsVersion,
        ["os_build"] = hw.WindowsBuild,
        ["os_display_version"] = hw.WindowsDisplayVersion,
        ["os_architecture"] = hw.Architecture,
        ["installed"] = hw.Installed,
        ["last_boot"] = hw.LastBoot,
        ["nics"] = hw.Nics,
        ["disks"] = hw.Disks,
        ["memory_modules"] = hw.MemoryModules,
        ["software"] = hw.Software,
    };
}
