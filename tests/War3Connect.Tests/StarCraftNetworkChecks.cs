using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using War3Connect.Agent;
using War3Connect.Core;

namespace War3Connect.Tests;

internal static class StarCraftNetworkChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        check(Convert.ToHexString(StarCraftDiscoveryProbe.Query()) == "D9FA14000200000050584553D300000000000000", "StarCraft experimental query matches published wire sample");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var target = (IPEndPoint)responder.Client.LocalEndPoint!;
        var query = StarCraftDiscoveryProbe.QueryAsync(target, cancellationToken: deadline.Token);
        var received = await responder.ReceiveAsync(deadline.Token);
        check(received.RemoteEndPoint.Port != target.Port && received.Buffer.SequenceEqual(StarCraftDiscoveryProbe.Query()), "probe uses separate random UDP source port");
        byte[] candidate = Convert.FromHexString("000014000000000050584553D300000000000000");
        await responder.SendAsync(candidate, received.RemoteEndPoint, deadline.Token);
        await responder.SendAsync(new byte[] { 1, 2, 3 }, received.RemoteEndPoint, deadline.Token);
        var result = await query;
        check(result.Replies.Length == 2 && result.Replies[0].MatchesRecordedHeader && !result.Replies[1].MatchesRecordedHeader, "probe preserves candidate and unknown raw replies without claiming compatibility");
        check(!StarCraftDiscoveryProbe.MatchesRecordedHeader(StarCraftDiscoveryProbe.Query()) && !StarCraftDiscoveryProbe.MatchesRecordedHeader(candidate[..^1]), "query and truncated packet are not classified as announcement candidates");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, deadline.Token);
        using var server = await listener.AcceptTcpClientAsync(deadline.Token);
        using var wire = WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan);
        using var remote = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        using var game = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var local = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var bridge = UdpSocketBridge.RunAsync(local, (IPEndPoint)game.Client.LocalEndPoint!, wire, stop.Token);
        try
        {
            await other.SendAsync(new byte[] { 99 }, (IPEndPoint)local.Client.LocalEndPoint!, deadline.Token);
            foreach (int size in new[] { 0, 7, 60000 })
            {
                var data = Enumerable.Range(0, size).Select(i => (byte)(i * 17)).ToArray();
                await game.SendAsync(data, (IPEndPoint)local.Client.LocalEndPoint!, deadline.Token);
                var buffer = new byte[65508];
                int count = 0;
                WebSocketReceiveResult frame;
                do
                {
                    frame = await remote.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), deadline.Token);
                    count += frame.Count;
                } while (!frame.EndOfMessage);
                check(frame.MessageType == WebSocketMessageType.Binary && count == size && buffer.AsSpan(0, count).SequenceEqual(data), $"UDP to WS preserves {size}-byte datagram and rejects unrelated sender");
            }
            await remote.SendAsync(new ArraySegment<byte>(new byte[] { 1, 2 }), WebSocketMessageType.Binary, false, deadline.Token);
            await remote.SendAsync(new ArraySegment<byte>(new byte[] { 3, 4 }), WebSocketMessageType.Binary, true, deadline.Token);
            check((await game.ReceiveAsync(deadline.Token)).Buffer.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "fragmented WS message becomes exactly one UDP datagram");
            await remote.SendAsync(new ArraySegment<byte>(Array.Empty<byte>()), WebSocketMessageType.Binary, true, deadline.Token);
            check((await game.ReceiveAsync(deadline.Token)).Buffer.Length == 0, "empty WS datagram forwarded to UDP");
            await remote.SendAsync(new ArraySegment<byte>(new byte[] { 1 }), WebSocketMessageType.Text, true, deadline.Token);
            try { await bridge; throw new Exception("text frame accepted"); }
            catch (IOException) { check(true, "UDP bridge rejects text frames and terminates"); }
        }
        finally
        {
            stop.Cancel();
            try { await bridge; } catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
    }
}
