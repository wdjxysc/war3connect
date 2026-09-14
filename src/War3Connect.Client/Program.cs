using Avalonia;
using Avalonia.Headless;

namespace War3Connect.Client;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var builder = AppBuilder.Configure<App>();
        builder = args.Contains("--smoke-test")
            ? builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia()
            : builder.UsePlatformDetect();
        return builder.StartWithClassicDesktopLifetime(args);
    }
}
