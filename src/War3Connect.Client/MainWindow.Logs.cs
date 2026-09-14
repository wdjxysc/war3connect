using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace War3Connect.Client;

public partial class MainWindow
{
    private readonly Queue<string> _logs = new();
    private bool _logsExpanded;
    private GridLength _roomHeight, _logHeight;

    private void AppendLog(string message)
    {
        if (message.Length > 4096) message = message[..4096] + "…（内容已截断）";
        _logs.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (_logs.Count > 500) _logs.Dequeue();
        RenderLogs();
    }

    private void RenderLogs()
    {
        if (LogBox == null || LogSummaryText == null) return;
        string filter = LogFilterBox.Text?.Trim() ?? "";
        var visible = _logs.Where(line => line.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var scroll = LogBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        var offset = scroll?.Offset;
        int start = LogBox.SelectionStart, end = LogBox.SelectionEnd;
        string text = string.Join(Environment.NewLine, visible);
        if (LogBox.Text != text)
        {
            LogBox.Text = text;
            LogBox.SelectionStart = Math.Min(start, text.Length);
            LogBox.SelectionEnd = Math.Min(end, text.Length);
        }
        LogSummaryText.Text = $"显示 {visible.Length} / {_logs.Count} 条 · 保留最近 500 条" +
            (AutoScrollBox.IsChecked == true ? " · 跟随最新" : " · 已暂停跟随");
        CopyLogButton.IsEnabled = ExportLogButton.IsEnabled = visible.Length > 0;
        ClearLogButton.IsEnabled = _logs.Count > 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (AutoScrollBox.IsChecked == true) FollowLatestLog();
            else if (scroll != null && offset.HasValue) scroll.Offset = offset.Value;
        }, DispatcherPriority.Loaded);
    }

    private void FollowLatestLog()
    {
        LogBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToEnd();
    }

    private void LogFilterChanged(object? sender, TextChangedEventArgs e) => RenderLogs();
    private void FollowLogsClick(object? sender, RoutedEventArgs e) => RenderLogs();
    private void ClearLogsClick(object? sender, RoutedEventArgs e) { _logs.Clear(); RenderLogs(); }

    private async void CopyLogsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("当前环境不支持剪贴板。");
            await clipboard.SetTextAsync(LogBox.Text ?? "");
            StatusText.Text = "已复制当前筛选的日志。";
        }
        catch (Exception ex) { StatusText.Text = "复制失败：" + ex.Message; }
    }

    private async void ExportLogsClick(object? sender, RoutedEventArgs e)
    {
        string snapshot = LogBox.Text ?? "";
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出连接日志", SuggestedFileName = $"War3Connect-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
                DefaultExtension = "txt", FileTypeChoices = [new("文本文件") { Patterns = ["*.txt"] }]
            });
            if (file == null) return;
            using (file)
            await using (var stream = await file.OpenWriteAsync())
            {
                if (stream.CanSeek) stream.SetLength(0);
                await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                await writer.WriteAsync(snapshot);
            }
            StatusText.Text = "已导出当前筛选的日志。";
        }
        catch (Exception ex) { StatusText.Text = "导出失败：" + ex.Message; }
    }

    private void ExpandLogsClick(object? sender, RoutedEventArgs e)
    {
        _logsExpanded = !_logsExpanded;
        if (_logsExpanded)
        {
            _roomHeight = WorkspaceGrid.RowDefinitions[0].Height;
            _logHeight = WorkspaceGrid.RowDefinitions[2].Height;
        }
        RoomWorkspacePanel.IsVisible = LogSplitter.IsVisible = !_logsExpanded;
        WorkspaceGrid.RowDefinitions[0].MinHeight = _logsExpanded ? 0 : 240;
        WorkspaceGrid.RowDefinitions[0].Height = _logsExpanded ? new GridLength(0) : _roomHeight;
        WorkspaceGrid.RowDefinitions[1].Height = new GridLength(_logsExpanded ? 0 : 12);
        WorkspaceGrid.RowDefinitions[2].Height = _logsExpanded ? new GridLength(1, GridUnitType.Star) : _logHeight;
        ExpandLogButton.Content = _logsExpanded ? "恢复布局" : "展开查看";
    }

    internal void VerifyLogControls()
    {
        for (int i = 0; i < 505; i++) AppendLog($"测试连接事件 {i}");
        if (_logs.Count != 500 || _logs.First().EndsWith("事件 0")) throw new Exception("日志保留上限错误");
        LogFilterBox.Text = "事件 504";
        RenderLogs();
        if (LogBox.Text?.Contains("事件 504") != true || LogBox.Text.Contains("事件 503")) throw new Exception("日志筛选失败");
        ExpandLogsClick(this, new RoutedEventArgs());
        if (RoomWorkspacePanel.IsVisible) throw new Exception("日志展开失败");
        ExpandLogsClick(this, new RoutedEventArgs());
        if (!RoomWorkspacePanel.IsVisible) throw new Exception("日志布局恢复失败");
        LogFilterBox.Text = "";
        ClearLogsClick(this, new RoutedEventArgs());
        if (_logs.Count != 0 || !string.IsNullOrEmpty(LogBox.Text)) throw new Exception("清空日志失败");
    }

    internal void PrepareLogPreview()
    {
        AppendLog("[界面演示] 服务器连接成功，已登录平台。");
        AppendLog("[界面演示] 已加入房间，等待房主游戏公告。");
        AppendLog("[界面演示] 隧道连接已建立。");
        AppendLog("[界面演示] 发现局域网主机，已广播到本机游戏。");
        AppendLog("[界面演示] 游戏连接已建立，正在转发数据。");
        ExpandLogsClick(this, new RoutedEventArgs());
    }
}
