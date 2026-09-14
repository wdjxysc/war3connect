using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using War3Connect.Agent;

namespace War3Connect.Client;
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool smoke = desktop.Args is ["--smoke-test", _];
            var window = new MainWindow(!smoke);
            desktop.MainWindow = window;
            if (smoke) Dispatcher.UIThread.Post(() =>
            {
                var path = Path.GetFullPath(desktop.Args![1]);
                try
                {
                    if (window.FindControl<Button>("CreateButton")!.IsEnabled) throw new Exception("创建按钮状态错误");
                    if (window.FindControl<TextBox>("ServerBox")!.Text != ClientConfiguration.LoadServerUrl(Path.Combine(AppContext.BaseDirectory, "clientsettings.json"))) throw new Exception("服务器配置加载失败");
                    if (!window.FindControl<Button>("SaveServerButton")!.IsEnabled) throw new Exception("服务器保存按钮状态错误");
                    if (window.FindControl<StackPanel>("WinePanel")!.IsVisible != OperatingSystem.IsLinux()) throw new Exception("Wine 设置平台状态错误");
                    window.UpdateLayout();
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
                    bitmap.Render(window);
                    bitmap.Save(path);
                    desktop.Shutdown(0);
                }
                catch (Exception e) { File.WriteAllText(path + ".error.txt", e.ToString()); desktop.Shutdown(1); }
            }, DispatcherPriority.ApplicationIdle);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
