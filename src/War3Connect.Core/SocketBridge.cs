using System.Net.Sockets;
using System.Net.WebSockets;

namespace War3Connect.Core;

public static class SocketBridge
{
    public static async Task RunAsync(TcpClient tcp, WebSocket socket, CancellationToken cancellationToken)
    {
        tcp.NoDelay = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stream = tcp.GetStream();
        async Task Upload()
        {
            var buffer = new byte[16384];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, stop.Token);
                if (count == 0) return;
                await socket.SendAsync(buffer.AsMemory(0, count), WebSocketMessageType.Binary, true, stop.Token);
            }
        }
        async Task Download()
        {
            var buffer = new byte[16384];
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), stop.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType != WebSocketMessageType.Binary) throw new IOException("中继收到非法消息。");
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), stop.Token);
            }
        }
        var upload = Upload();
        var download = Download();
        try { await await Task.WhenAny(upload, download); }
        finally
        {
            stop.Cancel();
            socket.Abort();
            tcp.Close();
            try { await Task.WhenAll(upload, download); }
            catch (Exception e) when (e is OperationCanceledException or IOException or WebSocketException or ObjectDisposedException) { }
        }
    }
}
