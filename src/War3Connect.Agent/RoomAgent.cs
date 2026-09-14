using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using War3Connect.Core;

namespace War3Connect.Agent;

/// <summary>Owns all sockets for exactly one platform room; cancellation never kills the game process.</summary>
public sealed class RoomAgent : IAsyncDisposable
{
    private readonly PlatformClient _api;
    private readonly GameInstallation _installation;
    private readonly string _room;
    private readonly bool _host;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<long, Task> _connections = new();
    private readonly HashSet<string> _accepted = [];
    private readonly List<Task> _loops = [];
    private readonly UdpClient _announcer = new(AddressFamily.InterNetwork);
    private TcpListener? _listener;
    private LanGame? _localGame;
    private uint? _advertisedCounter;
    private long _connectionId;
    private int _activeConnections;
    private string? _lastError;
    public event Action<RoomView>? Updated;
    public event Action<string>? Log;
    public event Action<string>? Closed;
    public int ActiveConnections => Volatile.Read(ref _activeConnections);
    public int LocalPort => _listener == null ? 0 : ((IPEndPoint)_listener.LocalEndpoint).Port;

    public RoomAgent(PlatformClient api, RoomView room, GameInstallation installation)
    {
        _api = api;
        _room = room.Id;
        _host = room.HostId == api.Session?.UserId;
        _installation = installation;
    }
    public void Start()
    {
        if (_loops.Count > 0) throw new InvalidOperationException("代理已经启动。");
        if (!_host)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(12);
            _loops.Add(AcceptPlayers());
            Emit($"本地游戏代理已启动，端口 {LocalPort}。请在 War3 的局域网列表中加入。");
        }
        else
        {
            _loops.Add(Discover());
            _loops.Add(AcceptHostRequests());
            Emit("等待房主在 War3 → 局域网中创建游戏（游戏端口设为 6112）。");
        }
        _loops.Add(RefreshRoom());
    }
    private async Task RefreshRoom()
    {
        string? verifiedMap = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var room = await _api.Get<RoomView>($"api/rooms/{_room}", _stop.Token);
                Updated?.Invoke(room);
                if (!_host)
                {
                    if (room.Game == null) await RemoveAdvertisement();
                    else
                    {
                        var game = LanProtocol.Parse(Convert.FromBase64String(room.Game.Packet));
                        string mapIdentity = $"{game.MapPath}:{File.GetLastWriteTimeUtc(_installation.MapFile).Ticks}";
                        if (mapIdentity != verifiedMap)
                        {
                            if (!await _installation.MatchesAdvertisement(game, _stop.Token))
                                throw new InvalidDataException($"请将一致的地图放到游戏目录的 {game.MapPath} 后重新加入。");
                            verifiedMap = mapIdentity;
                        }
                        if (_advertisedCounter != null && _advertisedCounter != game.HostCounter) await RemoveAdvertisement();
                        await Announce(LanProtocol.WithPort(game, (ushort)LocalPort));
                        _advertisedCounter = game.HostCounter;
                    }
                }
            }
            catch (PlatformException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                Closed?.Invoke(e.Message);
                _stop.Cancel();
                break;
            }
            catch (Exception e) when (Expected(e))
            {
                Report(e);
                if (!_host) await RemoveAdvertisement();
            }
            await Delay(2000);
        }
    }
    private async Task Discover()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string? verified = null;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await udp.SendAsync(LanProtocol.Search(), new IPEndPoint(IPAddress.Loopback, LanProtocol.DiscoveryPort), _stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(1000);
                var received = await udp.ReceiveAsync(timeout.Token);
                if (!IPAddress.IsLoopback(received.RemoteEndPoint.Address) || received.RemoteEndPoint.Port != LanProtocol.DiscoveryPort) continue;
                var game = LanProtocol.Parse(received.Buffer);
                string identity = $"{game.HostCounter}:{game.EntryKey}:{game.MapPath}:{File.GetLastWriteTimeUtc(_installation.MapFile).Ticks}";
                if (identity != verified)
                {
                    if (!await _installation.MatchesAdvertisement(game, _stop.Token))
                        throw new InvalidDataException("当前游戏地图与平台房间选择的文件不一致，请重新建图。");
                    verified = identity;
                    Emit($"发现游戏：{game.Name}，地图：{game.MapPath}。");
                }
                Volatile.Write(ref _localGame, game);
                await _api.Post($"api/rooms/{_room}/game", new PublishGame(Convert.ToBase64String(game.Packet)), _stop.Token);
            }
            catch (OperationCanceledException) { /* A silent discovery timeout is normal while loading/in game. */ }
            catch (Exception e) when (Expected(e)) { Report(e); }
            await Delay(1000);
        }
    }
    private async Task AcceptHostRequests()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var pending = await _api.Get<TunnelView[]>($"api/rooms/{_room}/tunnels", _stop.Token);
                foreach (var tunnel in pending)
                    if (_accepted.Add(tunnel.Id)) Track(() => HostConnection(tunnel.Id));
            }
            catch (Exception e) when (Expected(e)) { Report(e); }
            await Delay(350);
        }
    }
    private async Task HostConnection(string tunnel)
    {
        var game = Volatile.Read(ref _localGame) ?? throw new IOException("本地游戏尚未就绪。");
        using var tcp = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        // Never accept an arbitrary remote IP/port from a platform member.
        await tcp.ConnectAsync(IPAddress.Loopback, game.Port, timeout.Token);
        using var ws = await _api.ConnectTunnel(tunnel, _stop.Token);
        Emit("一名玩家已连接到本地游戏。");
        await SocketBridge.RunAsync(tcp, ws, _stop.Token);
    }
    private async Task AcceptPlayers()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var tcp = await _listener!.AcceptTcpClientAsync(_stop.Token);
                if (ActiveConnections > 0) { tcp.Dispose(); continue; }
                Track(async () =>
                {
                    using (tcp)
                    {
                        var tunnel = await _api.Post<TunnelView>($"api/rooms/{_room}/tunnels", new { }, _stop.Token);
                        using var ws = await _api.ConnectTunnel(tunnel.Id, _stop.Token);
                        Emit("已连接中继，正在加入房主游戏。");
                        await SocketBridge.RunAsync(tcp, ws, _stop.Token);
                    }
                });
            }
        }
        catch (Exception e) when (Expected(e)) { Report(e); }
    }
    private void Track(Func<Task> action)
    {
        var id = Interlocked.Increment(ref _connectionId);
        Interlocked.Increment(ref _activeConnections);
        async Task Run()
        {
            try { await action(); }
            catch (Exception e) when (Expected(e)) { Report(e); }
            finally { Interlocked.Decrement(ref _activeConnections); Emit("游戏连接已结束。"); }
        }
        var task = Run();
        _connections[id] = task;
        _ = task.ContinueWith(_ => { _connections.TryRemove(id, out var ignored); }, TaskScheduler.Default);
    }
    private async Task Announce(byte[] packet) => await _announcer.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, LanProtocol.DiscoveryPort), _stop.Token);
    private async Task RemoveAdvertisement()
    {
        if (_advertisedCounter is not uint counter) return;
        _advertisedCounter = null;
        try { await _announcer.SendAsync(LanProtocol.Remove(counter), new IPEndPoint(IPAddress.Loopback, LanProtocol.DiscoveryPort)); }
        catch (SocketException) { }
    }
    private void Emit(string message) => Log?.Invoke(message);
    private void Report(Exception e)
    {
        if (_stop.IsCancellationRequested || e is OperationCanceledException) return;
        if (_lastError == e.Message) return;
        _lastError = e.Message;
        Emit(e.Message);
    }
    private async Task Delay(int milliseconds)
    {
        try { await Task.Delay(milliseconds, _stop.Token); } catch (OperationCanceledException) { }
    }
    private static bool Expected(Exception e) => e is IOException or SocketException or WebSocketException or HttpRequestException
        or OperationCanceledException or ObjectDisposedException or PlatformException or FormatException;
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener?.Stop();
        await Task.WhenAll(_loops);
        await Task.WhenAll(_connections.Values.ToArray());
        await RemoveAdvertisement();
        _announcer.Dispose();
        _stop.Dispose();
    }
}
