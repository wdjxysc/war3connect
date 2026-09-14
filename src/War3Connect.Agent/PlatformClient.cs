using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using War3Connect.Core;

namespace War3Connect.Agent;

public sealed class PlatformException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
public sealed class PlatformClient : IDisposable
{
    private readonly HttpClient _http;
    public LoginResult? Session { get; private set; }
    public Uri Address { get; }
    public PlatformClient(string address, bool allowInsecureHttp = false)
    {
        Address = ParseAddress(address, allowInsecureHttp);
        _http = new() { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(10) };
    }
    public static Uri ParseAddress(string? address, bool allowInsecureHttp = false)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https") || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("请输入服务器根地址，如 https://play.example.com。");
        if (uri.Scheme == "http" && !uri.IsLoopback && !allowInsecureHttp)
            throw new ArgumentException("此地址使用未加密 HTTP。如需连接，请勾选“允许 HTTP（无证书）”；建议使用 HTTPS。");
        return uri;
    }
    public async Task LoginAsync(string username, string password, bool register, CancellationToken ct = default)
    {
        RequireCompatibleServer(await Get<HealthView>("health", ct));
        Session = await Post<LoginResult>(register ? "api/register" : "api/login", new Credentials(username, password), ct);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
    }
    public static void RequireCompatibleServer(HealthView health)
    {
        if (health.ProtocolVersion != Versions.ProtocolVersion)
            throw new InvalidOperationException($"客户端与服务端协议不兼容（需要 {Versions.ProtocolVersion}，服务端为 {health.ProtocolVersion}），请同步更新客户端和服务端。");
    }
    public async Task<T> Get<T>(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(path, ct);
        await Check(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new IOException("服务器响应为空。");
    }
    public async Task<T> Post<T>(string path, object body, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, body, ct);
        await Check(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new IOException("服务器响应为空。");
    }
    public async Task Post(string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(path, body ?? new { }, ct);
        await Check(response, ct);
    }
    private static async Task Check(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string message = $"服务器请求失败（{(int)response.StatusCode}）。";
        try { message = (await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken: ct))?.Error ?? message; }
        catch (JsonException) { }
        if (response.StatusCode == HttpStatusCode.TooManyRequests) message = "请求过于频繁，请稍后重试。";
        throw new PlatformException(response.StatusCode, message);
    }
    public async Task<ClientWebSocket> ConnectTunnel(string id, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + (Session?.Token ?? throw new InvalidOperationException("尚未登录。")));
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var uri = new UriBuilder(new Uri(Address, "relay/" + id)) { Scheme = Address.Scheme == "https" ? "wss" : "ws" };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        try { await ws.ConnectAsync(uri.Uri, timeout.Token); return ws; }
        catch { ws.Dispose(); throw; }
    }
    public void Dispose() => _http.Dispose();
}
