using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using War3Connect.Agent;
using War3Connect.Core;

namespace War3Connect.Client;

public partial class MainWindow : Window
{
    private sealed record Settings(string Server, string Username, string Game, string Map);
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "War3Connect", "settings.json");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private PlatformClient? _api;
    private RoomAgent? _agent;
    private RoomView? _room;
    private string _defaultServerAddress = ClientConfiguration.DefaultServerUrl;
    private bool _busy, _refreshing, _closing;
    public MainWindow(bool loadSettings = true)
    {
        InitializeComponent();
        LoadDefaultServerAddress();
        ServerBox.Text = _defaultServerAddress;
        try
        {
            if (loadSettings && File.Exists(_settingsPath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) is { } s)
            {
                UsernameBox.Text = s.Username; GamePathBox.Text = s.Game; MapPathBox.Text = s.Map;
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
        GameBrowseButton.IsEnabled = MapBrowseButton.IsEnabled = !_busy && _room == null;
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
        var api = new PlatformClient(ServerBox.Text);
        try { await api.LoginAsync(UsernameBox.Text.Trim(), PasswordBox.Password, register); }
        catch { api.Dispose(); throw; }
        _api?.Dispose();
        _api = api;
        PasswordBox.Clear();
        AccountText.Text = "已登录 · " + api.Session!.Username;
        StatusText.Text = "选择游戏和地图后，可以创建或加入房间。";
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
    private void BrowseGameClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择经典 War3 1.27 的 war3.exe", Filter = "游戏主程序|war3.exe" };
        if (dialog.ShowDialog(this) == true) { GamePathBox.Text = dialog.FileName; SaveSettings(); }
    }
    private void BrowseMapClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Maps 文件夹中的地图", Filter = "War3 地图|*.w3x;*.w3m" };
        var root = Path.GetDirectoryName(GamePathBox.Text);
        if (root != null && Directory.Exists(Path.Combine(root, "Maps"))) dialog.InitialDirectory = Path.Combine(root, "Maps");
        if (dialog.ShowDialog(this) == true) { MapPathBox.Text = dialog.FileName; SaveSettings(); }
    }
    private async Task<GameInstallation> Inspect()
    {
        if (string.IsNullOrWhiteSpace(GamePathBox.Text) || string.IsNullOrWhiteSpace(MapPathBox.Text)) throw new InvalidOperationException("请先选择游戏和地图。");
        StatusText.Text = "正在检查游戏版本和地图文件……";
        var install = await GameInstallation.Inspect(GamePathBox.Text, MapPathBox.Text);
        VersionText.Text = $"TFT {install.Version}\n地图校验 {install.MapHash[..12]}…";
        SaveSettings();
        return install;
    }
    private async void LaunchClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var install = await Inspect();
        install.Launch();
        StatusText.Text = "已启动游戏，请进入局域网；游戏端口使用 6112。";
    });
    private async void CreateClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireLogin();
        var install = await Inspect();
        var room = await _api!.Post<RoomView>("api/rooms", new CreateRoom(RoomNameBox.Text.Trim(), RoomPasswordBox.Password, install.Version, Path.GetFileName(install.MapFile), install.MapHash));
        await Enter(room, install);
    });
    private async void JoinClick(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireLogin();
        var selected = RoomsList.SelectedItem as RoomView ?? throw new InvalidOperationException("请先在大厅选中一个房间。");
        var install = await Inspect();
        var room = await _api!.Post<RoomView>($"api/rooms/{selected.Id}/join", new JoinRoom(RoomPasswordBox.Password, install.Version, install.MapHash));
        await Enter(room, install);
    });
    private void RequireLogin()
    {
        if (_api?.Session == null) throw new InvalidOperationException("请先登录。");
        if (_room != null) throw new InvalidOperationException("请先退出当前房间。");
    }
    private async Task Enter(RoomView room, GameInstallation install)
    {
        _room = room;
        var agent = new RoomAgent(_api!, room, install);
        _agent = agent;
        agent.Log += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        agent.Updated += updated => Dispatcher.BeginInvoke(() => { if (_agent == agent) ShowRoom(updated); });
        agent.Closed += message => Dispatcher.BeginInvoke(async () =>
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
        StatusText.Text = room.HostId == _api!.Session!.UserId ? "房间已创建。请启动 War3，在局域网中使用所选地图建图。" : "已加入房间。请启动 War3，在局域网列表中等待并加入房主游戏。";
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
        if (MessagesBox.Text != messages) { MessagesBox.Text = messages; MessagesBox.ScrollToEnd(); }
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
        var text = ChatBox.Text.Trim();
        if (text.Length == 0) return;
        await _api.Post($"api/rooms/{_room.Id}/chat", new SendChat(text));
        ChatBox.Clear();
    }
    private bool SaveSettings()
    {
        try
        {
            var address = PlatformClient.ParseAddress(ServerBox.Text).AbsoluteUri.TrimEnd('/');
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var content = JsonSerializer.Serialize(new Settings(address, UsernameBox.Text, GamePathBox.Text, MapPathBox.Text));
            File.WriteAllText(_settingsPath + ".tmp", content);
            File.Move(_settingsPath + ".tmp", _settingsPath, true);
            ServerBox.Text = address;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { StatusText.Text = "无法保存设置：" + e.Message; AppendLog(StatusText.Text); return false; }
    }
    private void AppendLog(string message)
    {
        if (LogBox.Text.Length > 24000) LogBox.Text = LogBox.Text[^12000..];
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
    private async void WindowClosing(object? sender, CancelEventArgs e)
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
