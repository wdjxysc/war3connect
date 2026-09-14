using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace War3Connect.Client;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length != 2 || e.Args[0] != "--smoke-test")
        {
            new MainWindow().Show();
            return;
        }
        var window = new MainWindow(loadSettings: false) { ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
        window.Show();
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            try
            {
                if (window.FindName("CreateButton") is not Button create || create.IsEnabled)
                    throw new InvalidOperationException("未登录时创建按钮应被禁用。");
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.GetFullPath(e.Args[1]));
                encoder.Save(output);
                Shutdown(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.GetFullPath(e.Args[1]) + ".error.txt", error.ToString());
                Shutdown(1);
            }
        });
    }
}
