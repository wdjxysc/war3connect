using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace War3Connect.Core;

/// <summary>
/// One fixed local game endpoint and one peer tunnel. Each binary WebSocket message
/// carries exactly one UDP datagram, including empty datagrams. Does not interpret
/// StarCraft packets or resolve addresses embedded in game payloads.
/// </summary>
public static class UdpSocketBridge
{
    public const int MaxDatagramLength = 65507;

    public static async Task RunAsync(UdpClient local, IPEndPoint game, WebSocket tunnel, CancellationToken cancellationToken)
    {
        if (game.AddressFamily != AddressFamily.InterNetwork || !IPAddress.IsLoopback(game.Address) || game.Port == 0)
            throw new ArgumentException("UDP 代理仅允许固定的本机 IPv4 游戏端点。", nameof(game));
        if (local.Client.LocalEndPoint is not IPEndPoint bound || !IPAddress.IsLoopback(bound.Address))
            throw new ArgumentException("UDP 代理必须绑定本机回环地址。", nameof(local));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        async Task Upload()
        {
            while (true)
            {
                var packet = await local.ReceiveAsync(stop.Token);
                if (!packet.RemoteEndPoint.Equals(game)) continue;
                await tunnel.SendAsync(packet.Buffer.AsMemory(), WebSocketMessageType.Binary, true, stop.Token);
            }
        }
        async Task Download()
        {
            var buffer = new byte[MaxDatagramLength + 1];
            while (true)
            {
                int length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await tunnel.ReceiveAsync(buffer.AsMemory(length), stop.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (result.MessageType != WebSocketMessageType.Binary) throw new IOException("UDP 隧道仅接受二进制数据报。");
                    length += result.Count;
                    if (length > MaxDatagramLength) throw new IOException("UDP 数据报超过 IPv4 长度上限。");
                } while (!result.EndOfMessage);
                await local.SendAsync(buffer.AsMemory(0, length), game, stop.Token);
            }
        }
        var upload = Upload();
        var download = Download();
        try { await await Task.WhenAny(upload, download); }
        finally
        {
            stop.Cancel();
            tunnel.Abort();
            try { await Task.WhenAll(upload, download); }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or WebSocketException or ObjectDisposedException) { }
        }
    }
}
