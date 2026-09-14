using Avalonia.Controls;
using War3Connect.Core;
using War3Connect.Agent;
using Avalonia.Interactivity;
using System.Net;

namespace War3Connect.Client;

public partial class MainWindow
{
    private const string StarCraftNotice = "星际准备房间：支持成员与聊天，游戏联机转发尚未接入。";
    private Dictionary<string, GamePreferences> _gamePreferences = new();
    private string _selectedGame = GameCatalog.War3;
    private bool _gameUiReady;
    private bool IsStarCraft => _selectedGame == GameCatalog.StarCraft;

    private void RememberGamePreferences() => _gamePreferences[_selectedGame] = new(GamePathBox.Text ?? "", WineBox.Text, WinePrefixBox.Text);

    private void LoadGamePreferences()
    {
        var settings = _gamePreferences.GetValueOrDefault(_selectedGame) ?? new GamePreferences("");
        GamePathBox.Text = settings.Path;
        WineBox.Text = settings.Wine ?? "wine";
        WinePrefixBox.Text = settings.WinePrefix ?? "";
        GameBadge.Text = IsStarCraft ? "BROOD WAR  /  1.16.1" : "CLASSIC TFT  /  1.27";
        GameDescription.Text = IsStarCraft ? "星际争霸：母巢之战 1.16.1" : "选择经典 TFT 1.27 的游戏程序。";
        GamePathBox.Watermark = "尚未选择 " + GameCatalog.Executable(_selectedGame);
        GameBrowseButton.Content = "选择 " + GameCatalog.Executable(_selectedGame);
        LaunchButton.Content = IsStarCraft ? "启动星际争霸" : "启动 War3";
        VersionText.Text = "选择程序或入房时检查完整版本号。";
        GameSteps.Text = IsStarCraft ? "01  选择 StarCraft.exe\n\n02  可创建准备房间并聊天\n\n03  跨网对战功能待接入" : "01  创建或加入平台房间\n\n02  房主在 War3 局域网中建图\n\n03  玩家从游戏内主机列表加入";
        GameNotice.Text = IsStarCraft ? StarCraftNotice : "地图由游戏内选择、下载和校验。对战期间请保持平台运行。";
        GameStatusText.Text = IsStarCraft ? StarCraftNotice : "加入平台房间后，在 War3 的局域网列表中进入游戏。";
        CreateButton.Content = IsStarCraft ? "创建准备房间" : "创建房间";
        JoinButton.Content = IsStarCraft ? "加入准备房间" : "加入选中房间";
        EmptyRoomTitle.Text = IsStarCraft ? "星际准备大厅" : "等待下一场对战";
        EmptyRoomHint.Text = IsStarCraft ? "可创建准备房间并聊天，跨网对战尚未接入。" : "登录后创建房间，邀请朋友一起加入。";
        ProbeStarCraftButton.IsVisible = IsStarCraft;
    }

    private async void GameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_gameUiReady) return;
        string next = GameSelector.SelectedIndex == 1 ? GameCatalog.StarCraft : GameCatalog.War3;
        if (next == _selectedGame) return;
        RememberGamePreferences();
        _selectedGame = next;
        LoadGamePreferences();
        RoomsList.ItemsSource = null;
        SaveSettings();
        StatusText.Text = IsStarCraft ? StarCraftNotice : "已切换至魔兽争霸 III：冰封王座。";
        if (_api?.Session != null) await Run(RefreshRooms);
    }

    internal void VerifyGameControls()
    {
        GamePathBox.Text = "war3-fixture.exe";
        WinePrefixBox.Text = "war3-prefix";
        GameSelector.SelectedIndex = 1;
        if (GamePathBox.Text != "" || !GameNotice.Text!.Contains("尚未接入")) throw new Exception("星际配置隔离或提示错误");
        GamePathBox.Text = "starcraft-fixture.exe";
        WinePrefixBox.Text = "starcraft-prefix";
        GameSelector.SelectedIndex = 0;
        if (GamePathBox.Text != "war3-fixture.exe" || WinePrefixBox.Text != "war3-prefix") throw new Exception("War3 配置丢失");
        GameSelector.SelectedIndex = 1;
        if (GamePathBox.Text != "starcraft-fixture.exe" || WinePrefixBox.Text != "starcraft-prefix") throw new Exception("星际配置丢失");
        _gamePreferences.Clear();
        GamePathBox.Text = "";
        WinePrefixBox.Text = "";
        GameSelector.SelectedIndex = 0;
    }

    private async void ProbeStarCraftClick(object? sender, RoutedEventArgs e) => await Run(async () =>
    {
        AppendLog("星际实验探测：请先在游戏 LAN（UDP）中创建主机并停留房间，查询使用本机随机端口。");
        var result = await StarCraftDiscoveryProbe.QueryAsync(new IPEndPoint(IPAddress.Loopback, StarCraftDiscoveryProbe.DiscoveryPort));
        AppendLog($"探测端点：{result.LocalEndpoint} → {result.Target}；回复 {result.Replies.Length} 个。");
        foreach (var reply in result.Replies)
            AppendLog($"来源 {reply.Source}；历史公告头匹配：{reply.MatchesRecordedHeader}；原始数据：{reply.PacketHex}");
        StatusText.Text = result.Replies.Length == 0
            ? "未在随机端口收到回复；不能据此判断游戏未建主机或必须使用驱动。"
            : "已收到 UDP 回复，详见日志；尚未验证公告重发、入房或对战。";
        AppendLog(StatusText.Text);
    });
}
