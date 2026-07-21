using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NetBoxWindowsAgent;

internal sealed class NetBoxClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public NetBoxClient(AgentConfig config)
    {
        var handler = new HttpClientHandler();
        if (!config.VerifyTls) handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        _http = new HttpClient(handler) { BaseAddress = new Uri(config.NetBoxUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", config.ApiToken);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NetBox-Windows-Agent/0.1.0");
    }

    public async Task<NetBoxObject?> FindOne(string endpoint, params (string Key, string? Value)[] filters)
    {
        var query = string.Join('&', filters.Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .Select(f => $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value!)}"));
        var page = await Send<Page<NetBoxObject>>(HttpMethod.Get, $"api/{endpoint}/?limit=2&{query}");
        if (page.Count > 1) throw new InvalidOperationException($"NetBox-Abfrage für {endpoint} ist nicht eindeutig.");
        return page.Results.SingleOrDefault();
    }

    public async Task<NetBoxObject> Create(string endpoint, object payload) =>
        await Send<NetBoxObject>(HttpMethod.Post, $"api/{endpoint}/", payload);
    public async Task<NetBoxObject> Patch(string endpoint, int id, object payload) =>
        await Send<NetBoxObject>(HttpMethod.Patch, $"api/{endpoint}/{id}/", payload);

    private async Task<T> Send<T>(HttpMethod method, string uri, object? payload = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (payload is not null) request.Content = JsonContent.Create(payload, options: _json);
        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"NetBox {method} {uri}: {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        return JsonSerializer.Deserialize<T>(body, _json) ?? throw new InvalidOperationException("NetBox lieferte eine leere Antwort.");
    }
    public void Dispose() => _http.Dispose();
}
