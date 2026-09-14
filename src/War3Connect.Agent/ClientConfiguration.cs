using System.Text.Json;

namespace War3Connect.Agent;

public sealed record ClientConfiguration(string ServerUrl)
{
    public const string DefaultServerUrl = "http://127.0.0.1:5080";

    public static string LoadServerUrl(string path)
    {
        if (!File.Exists(path)) return DefaultServerUrl;
        var config = JsonSerializer.Deserialize<ClientConfiguration>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("客户端配置内容为空。");
        return PlatformClient.ParseAddress(config.ServerUrl).AbsoluteUri.TrimEnd('/');
    }
}
