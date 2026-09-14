using Avalonia;
using Avalonia.Headless;

namespace War3Connect.Client;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--probe-starcraft", var target, var sourcePort, var output])
        {
            try
            {
                if (!System.Net.IPAddress.TryParse(target, out var ip) || !int.TryParse(sourcePort, out int port))
                    throw new ArgumentException("需要 IPv4 目标和本地源端口（0 为随机端口）。");
                var result = War3Connect.Agent.StarCraftDiscoveryProbe.QueryAsync(new(ip, 6111), localPort: port).GetAwaiter().GetResult();
                File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
        }
        var builder = AppBuilder.Configure<App>();
        builder = args.Contains("--smoke-test")
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
            : builder.UsePlatformDetect();
        return builder.StartWithClassicDesktopLifetime(args);
    }
}
