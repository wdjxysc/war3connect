using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Threading;

using War3Connect.Agent;
using War3Connect.Core;

namespace War3Connect.Client;

public partial class MainWindow : Window
{
    private sealed record Settings(string Server, string Username, string Game, string? Wine = null, string? WinePrefix = null);
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "War3Connect", "settings.json");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PlatformClient? _api;
    private RoomAgent? _agent;
    private RoomView? _room;
    private string _defaultServerAddress = ClientConfiguration.DefaultServerUrl;
    private bool _busy, _refreshing, _closing;
    public MainWindow() : this(true) { }
    public MainWindow(bool loadSettings)
    {
        InitializeComponent();
        WinePanel.IsVisible = OperatingSystem.IsLinux();
        WineBox.Text = "wine";
        LoadDefaultServerAddress();
        ServerBox.Text = _defaultServerAddress;
        try
        {
            if (loadSettings && File.Exists(_settingsPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) is { } s)
            {
                WineBox.Text = s.Wine ?? "wine"; WinePrefixBox.Text = s.WinePrefix ?? ""; UsernameBox.Text = s.Username; GamePathBox.Text = s.Game;
                if (!string.IsNullOrWhiteSpace(s.Server)) ServerBox.Text = PlatformClient.ParseAddress(s.Server).AbsoluteUri.TrimEnd('/');
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { AppendLog("设置读取失败，服务器地址使用默认配置：" + e.Message); }
        _timer.Tick += async (_, _) =>
        {
            if (_api?.Session != null && !_busy && !_refreshing)
            {
                try { await RefreshRooms(); }
                catch (Exception e) { StatusText.Text = "大厅刷新失败：" + e.Message; }
            }
        };
        _timer.Start();
        UpdateButtons();
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy || _closing) return;
        _busy = true;
        UpdateButtons();
        try { await action(); }
        catch (Exception e) { StatusText.Text = e.Message; AppendLog(e.Message); }
        finally { _busy = false; UpdateButtons(); }
    }
    private void UpdateButtons()
    {
        bool logged = _api?.Session != null;
        LoginButton.IsEnabled = RegisterButton.IsEnabled = !_busy && !logged;
        LogoutButton.IsEnabled = !_busy && logged;
        ServerBox.IsEnabled = UsernameBox.IsEnabled = PasswordBox.IsEnabled = !logged && !_busy;
        SaveServerButton.IsEnabled = DefaultServerButton.IsEnabled = !logged && !_busy;
        CreateButton.IsEnabled = JoinButton.IsEnabled = !_busy && logged && _room == null;
        GameBrowseButton.IsEnabled = !_busy && _room == null;
    }
    private async void LoginClick(object sender, RoutedEventArgs e) => await Run(() => Login(false));
    private async void RegisterClick(object sender, RoutedEventArgs e) => await Run(() => Login(true));
    private void LoadDefaultServerAddress()
    {
        try { _defaultServerAddress = ClientConfiguration.LoadServerUrl(Path.Combine(AppContext.BaseDirectory, "clientsettings.json")); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        { AppendLog("clientsettings.json 无效，保留可用默认地址：" + e.Message); }
    }
    private async void SaveServerClick(object sender, RoutedEventArgs e) => await Run(() =>
    {
        if (SaveSettings()) StatusText.Text = "服务器地址已保存，下次启动自动使用。";
        return Task.CompletedTask;
    });
    private async void DefaultServerClick(object sender, RoutedEventArgs e) => await Run(() =>
    {
        LoadDefaultServerAddress();
        ServerBox.Text = _defaultServerAddress;
        if (SaveSettings()) StatusText.Text = "已使用并保存默认服务器地址。";
        return Task.CompletedTask;
    });
    private async Task Login(bool register)
    {
        var api = new PlatformClient(ServerBox.Text ?? "");
        try { await api.LoginAsync((UsernameBox.Text ?? "").Trim(), (PasswordBox.Text ?? ""), register); }
        catch { api.Dispose(); throw; }
        _api?.Dispose();
        _api = api;
        PasswordBox.Text = "";
        AccountText.Text = "已登录 · " + api.Session!.Username;
        StatusText.Text = "选择游戏后，可以创建或加入平台房间；地图在 War3 内选择。";
        SaveSettings();
        await RefreshRooms();
    }
    private async void LogoutClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await Leave();
        if (_api != null)
        {
            try { await _api.Post("api/logout"); } finally { _api.Dispose(); _api = null; }
        }
        AccountText.Text = "未登录";
        RoomsList.ItemsSource = null;
        StatusText.Text = "已退出账号。";
    });
    private async void RefreshClick(object sender, RoutedEventArgs e) => await Run(RefreshRooms);
    private async Task RefreshRooms()
    {
        if (_api?.Session == null) throw new InvalidOperationException("请先登录。");
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var selectedId = (RoomsList.SelectedItem as RoomView)?.Id;
            var rooms = await _api.Get<RoomView[]>("api/rooms");
            RoomsList.ItemsSource = rooms;
            RoomsList.SelectedItem = rooms.FirstOrDefault(r => r.Id == selectedId);
        }
        finally { _refreshing = false; }
    }
    private async void BrowseGameClick(object sender, RoutedEventArgs e) => await PickFile(GamePathBox, ["*.exe", "*.EXE"]);
    private Task PickFile(TextBox target, string[] patterns) => Run(async () =>
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择 War3 游戏程序", FileTypeFilter = [new("War3") { Patterns = patterns }] });
        if (files.Count > 0) { target.Text = files[0].TryGetLocalPath(); SaveSettings(); }
    });
    private GameInstallation Inspect()
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text)) throw new InvalidOperationException("请先选择游戏。");
        StatusText.Text = "正在检查游戏版本……";
        var install = GameInstallation.Inspect(GamePathBox.Text);
        VersionText.Text = $"TFT {install.Version}\n地图选择、下载和校验由 War3 处理。";
        SaveSettings();
        return install;
    }
    private async void LaunchClick(object sender, RoutedEventArgs e) => await Run(() =>
    {
        var install = Inspect();
        install.Launch(WineBox.Text, WinePrefixBox.Text);
        StatusText.Text = "已启动游戏，请进入局域网；游戏端口使用 6112。";
        return Task.CompletedTask;
    });
    private async void CreateClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireLogin();
        var install = Inspect();
        var room = await _api!.Post<RoomView>("api/rooms", new CreateRoom((RoomNameBox.Text ?? "").Trim(), (RoomPasswordBox.Text ?? ""), install.Version));
        await Enter(room);
    });
    private async void JoinClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireLogin();
        var selected = RoomsList.SelectedItem as RoomView ?? throw new InvalidOperationException("请先在大厅选中一个房间。");
        var install = Inspect();
        var room = await _api!.Post<RoomView>($"api/rooms/{selected.Id}/join", new JoinRoom((RoomPasswordBox.Text ?? ""), install.Version));
        await Enter(room);
    });
    private void RequireLogin()
    {
        if (_api?.Session == null) throw new InvalidOperationException("请先登录。");
        if (_room != null) throw new InvalidOperationException("请先退出当前房间。");
    }
    private async Task Enter(RoomView room)
    {
        _room = room;
        var agent = new RoomAgent(_api!, room);
        _agent = agent;
        agent.Log += message => Dispatcher.UIThread.Post(() => AppendLog(message));
        agent.Updated += updated => Dispatcher.UIThread.Post(() => { if (_agent == agent) ShowRoom(updated); });
        agent.Closed += message => Dispatcher.UIThread.Post(async () =>
        {
            if (_agent != agent) return;
            await ClearRoom();
            StatusText.Text = message;
        });
        try { agent.Start(); }
        catch
        {
            await ClearRoom();
            await _api!.Post($"api/rooms/{room.Id}/leave");
            throw;
        }
        ShowRoom(room);
        StatusText.Text = room.HostId == _api!.Session!.UserId ? "房间已创建。请启动 War3，在局域网中选择地图建图。" : "已加入房间。请启动 War3，在局域网列表中等待并加入房主游戏。";
        await RefreshRooms();
    }
    private void ShowRoom(RoomView room)
    {
        _room = room;
        RoomTitle.Text = $"{room.Name}  ·  平台成员 {room.Members.Length}/{room.Capacity}";
        MembersText.Text = string.Join("  ·  ", room.Members.Select(m => m.Name + (m.IsHost ? "（房主）" : "")));
        GameStatusText.Text = room.Game == null
            ? $"当前无建图公告（可能尚未建图或已开始）。代理连接：{_agent?.ActiveConnections ?? 0}"
            : $"游戏：{room.Game.Name}  ·  代理连接：{_agent?.ActiveConnections ?? 0}（不代表游戏人数）";
        string messages = string.Join(Environment.NewLine, room.Messages.Select(m => $"{m.Time.ToLocalTime():HH:mm}  {m.Name}：{m.Text}"));
        if (MessagesBox.Text != messages) { MessagesBox.Text = messages; MessagesBox.CaretIndex = messages.Length; }
        UpdateButtons();
    }
    private async void LeaveClick(object sender, RoutedEventArgs e) => await Run(Leave);
    private async Task Leave()
    {
        var room = _room;
        await ClearRoom();
        if (room != null && _api != null)
        {
            try { await _api.Post($"api/rooms/{room.Id}/leave"); }
            catch (Exception e) { AppendLog("退出通知失败，服务器将在心跳超时后清理：" + e.Message); }
        }
        StatusText.Text = "已退出房间。";
    }
    private async Task ClearRoom()
    {
        var agent = _agent;
        _agent = null;
        _room = null;
        if (agent != null) await agent.DisposeAsync();
        RoomTitle.Text = "尚未加入房间";
        MembersText.Text = MessagesBox.Text = "";
        GameStatusText.Text = "房主建图后，玩家进入 War3 → 局域网加入。";
        UpdateButtons();
    }
    private async void SendClick(object sender, RoutedEventArgs e) => await Run(Send);
    private async void ChatKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await Run(Send); }
    }
    private async Task Send()
    {
        if (_room == null || _api == null) throw new InvalidOperationException("请先加入房间。");
        var text = (ChatBox.Text ?? "").Trim();
        if (text.Length == 0) return;
        await _api.Post($"api/rooms/{_room.Id}/chat", new SendChat(text));
        ChatBox.Text = "";
    }
    private bool SaveSettings()
    {
        try
        {
            var address = PlatformClient.ParseAddress(ServerBox.Text).AbsoluteUri.TrimEnd('/');
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var content = JsonSerializer.Serialize(new Settings(address, UsernameBox.Text ?? "", GamePathBox.Text ?? "", WineBox.Text, WinePrefixBox.Text));
            File.WriteAllText(_settingsPath + ".tmp", content);
            File.Move(_settingsPath + ".tmp", _settingsPath, true);
            ServerBox.Text = address;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { StatusText.Text = "无法保存设置：" + e.Message; AppendLog(StatusText.Text ?? ""); return false; }
    }
    private void AppendLog(string message)
    {
        if (LogBox.Text?.Length > 24000) LogBox.Text = LogBox.Text[^12000..];
        LogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
    }
    private async void WindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_busy) { StatusText.Text = "请等待当前操作完成后关闭。"; return; }
        _closing = true;
        _timer.Stop();
        await Leave();
        _api?.Dispose();
        Close();
    }
}
