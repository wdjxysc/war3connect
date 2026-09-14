using System.Text.Json;

namespace War3Connect.Agent;

public sealed record ClientConfiguration(string ServerUrl, bool AllowInsecureHttp = false)
{
    public const string DefaultServerUrl = "http://127.0.0.1:5080";

    public static string LoadServerUrl(string path) => Load(path).ServerUrl;

    public static ClientConfiguration Load(string path)
    {
        if (!File.Exists(path)) return new(DefaultServerUrl);
        var config = JsonSerializer.Deserialize<ClientConfiguration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("客户端配置内容为空。");
        return config with { ServerUrl = PlatformClient.ParseAddress(config.ServerUrl, config.AllowInsecureHttp).AbsoluteUri.TrimEnd('/') };
    }
}
