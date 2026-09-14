using System.Net.WebSockets;
using War3Connect.Core;

namespace War3Connect.Server;

public sealed class Relay(ILogger<Relay> logger, TimeProvider clock)
{
    private sealed class Tunnel(string room, string host, string guest, DateTimeOffset created)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Room { get; } = room;
        public string Host { get; } = host;
        public string Guest { get; } = guest;
        public DateTimeOffset Created { get; } = created;
        public WebSocket? HostSocket, GuestSocket;
        public bool Started;
        public HashSet<string> Reservations { get; } = [];
        public CancellationTokenSource Stop { get; } = new();
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Tunnel> _tunnels = [];
    private long _bytes;
    public RelayStats Stats { get { lock (_gate) return new(_tunnels.Count, Interlocked.Read(ref _bytes)); } }
    public TunnelView Create(string room, string host, string guest)
    {
        lock (_gate)
        {
            if (_tunnels.Values.Any(t => t.Room == room && t.Guest == guest)) throw new ApiException(409, "已有游戏连接，请先退出游戏中的房间。");
            if (_tunnels.Count >= 1000) throw new ApiException(503, "中继连接数已满。");
            var t = new Tunnel(room, host, guest, clock.GetUtcNow());
            _tunnels.Add(t.Id, t);
            return new(t.Id);
        }
    }
    public TunnelView[] Pending(string room)
    {
        lock (_gate) return _tunnels.Values.Where(t => t.Room == room && t.HostSocket == null && !t.Stop.IsCancellationRequested).Select(t => new TunnelView(t.Id)).ToArray();
    }
    public async Task Attach(HttpContext context, string id, string user)
    {
        Tunnel t;
        bool host;
        // Reserve the side before the asynchronous HTTP upgrade.
        lock (_gate)
        {
            if (!_tunnels.TryGetValue(id, out t!)) throw new ApiException(404, "连接已过期。");
            host = user == t.Host;
            if (!host && user != t.Guest) throw new ApiException(403, "无权连接该中继。");
            if (!context.WebSockets.IsWebSocketRequest) throw new ApiException(400, "需要 WebSocket 连接。");
            if (!t.Reservations.Add(user)) throw new ApiException(409, "连接已被使用。");
        }
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            using var abort = context.RequestAborted.Register(() => Cancel(t));
            lock (_gate)
            {
                if (t.Stop.IsCancellationRequested) { socket.Abort(); return; }
                if (host) t.HostSocket = socket; else t.GuestSocket = socket;
                if (t.HostSocket != null && t.GuestSocket != null && !t.Started)
                {
                    t.Started = true;
                    _ = Run(t);
                }
            }
            await t.Done.Task;
        }
        finally { Cancel(t); }
    }
    private async Task Run(Tunnel t)
    {
        logger.LogInformation("Relay connected {Tunnel} in room {Room}", t.Id, t.Room);
        async Task Pump(WebSocket from, WebSocket to)
        {
            var buffer = new byte[16384];
            long windowBytes = 0;
            var windowStart = Environment.TickCount64;
            while (!t.Stop.IsCancellationRequested)
            {
                var result = await from.ReceiveAsync(buffer.AsMemory(), t.Stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType != WebSocketMessageType.Binary) throw new IOException("Binary frames required");
                if (Environment.TickCount64 - windowStart > 1000) { windowStart = Environment.TickCount64; windowBytes = 0; }
                windowBytes += result.Count;
                // Map downloads can legitimately fill the link. Apply backpressure rather than disconnecting.
                if (windowBytes > 2 * 1024 * 1024)
                {
                    int delay = (int)Math.Max(1, 1000 - (Environment.TickCount64 - windowStart));
                    await Task.Delay(delay, t.Stop.Token);
                    windowStart = Environment.TickCount64;
                    windowBytes = result.Count;
                }
                if (result.Count > 0)
                {
                    await to.SendAsync(buffer.AsMemory(0, result.Count), WebSocketMessageType.Binary, true, t.Stop.Token);
                    Interlocked.Add(ref _bytes, result.Count);
                }
            }
        }
        var a = Pump(t.HostSocket!, t.GuestSocket!);
        var b = Pump(t.GuestSocket!, t.HostSocket!);
        try { await await Task.WhenAny(a, b); }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        { logger.LogDebug(e, "Relay ended {Tunnel}", t.Id); }
        finally
        {
            Cancel(t);
            try { await Task.WhenAll(a, b); } catch (Exception e) when (e is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException) { }
            logger.LogInformation("Relay disconnected {Tunnel}", t.Id);
        }
    }
    private void Cancel(Tunnel t)
    {
        lock (_gate)
        {
            _tunnels.Remove(t.Id);
            if (!t.Stop.IsCancellationRequested) t.Stop.Cancel();
            t.HostSocket?.Abort();
            t.GuestSocket?.Abort();
            t.Done.TrySetResult();
        }
    }
    public void CancelRoom(string room) { lock (_gate) foreach (var t in _tunnels.Values.Where(t => t.Room == room).ToArray()) Cancel(t); }
    public void CancelUser(string room, string user) { lock (_gate) foreach (var t in _tunnels.Values.Where(t => t.Room == room && t.Guest == user).ToArray()) Cancel(t); }
    public void Sweep()
    {
        lock (_gate) foreach (var t in _tunnels.Values.Where(t => !t.Started && clock.GetUtcNow() - t.Created > TimeSpan.FromSeconds(15)).ToArray()) Cancel(t);
    }
}
