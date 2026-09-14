using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace War3Connect.Agent;

public sealed record StarCraftProbeReply(string Source, string PacketHex, bool MatchesRecordedHeader);
public sealed record StarCraftProbeResult(string LocalEndpoint, string Target, StarCraftProbeReply[] Replies);

/// <summary>
/// Experimental query from tingar/udp-proxy's published wire capture. A matching
/// header is only a candidate response, not proof of 1.16.1 compatibility or joining.
/// </summary>
public static class StarCraftDiscoveryProbe
{
    public const int DiscoveryPort = 6111;
    public static byte[] Query() => Convert.FromHexString("D9FA14000200000050584553D300000000000000");

    public static bool MatchesRecordedHeader(ReadOnlySpan<byte> packet) => packet.Length >= 20
        && packet.Length <= 16384
        && BinaryPrimitives.ReadUInt16LittleEndian(packet[2..]) == packet.Length
        && BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]) <= 1
        && packet.Slice(8, 4).SequenceEqual("PXES"u8)
        && BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]) == 0xd3;

    public static async Task<StarCraftProbeResult> QueryAsync(IPEndPoint target, IPAddress? localAddress = null,
        int localPort = 0, CancellationToken cancellationToken = default)
    {
        if (target.AddressFamily != AddressFamily.InterNetwork || target.Port == 0)
            throw new ArgumentException("需要有效的 IPv4 UDP 目标。");
        localAddress ??= IPAddress.IsLoopback(target.Address) ? IPAddress.Loopback : IPAddress.Any;
        if (localAddress.AddressFamily != AddressFamily.InterNetwork || localPort is < 0 or > 65535)
            throw new ArgumentException("需要有效的 IPv4 本地绑定地址和端口。");
        using var udp = new UdpClient(AddressFamily.InterNetwork);
        // Never steal or share the game's occupied port. Fixed-port experiments
        // should run on a second machine where no game owns that endpoint.
        udp.ExclusiveAddressUse = true;
        udp.Client.Bind(new IPEndPoint(localAddress, localPort));
        var bound = udp.Client.LocalEndPoint!.ToString()!;
        var replies = new List<StarCraftProbeReply>();
        var query = Query();
        for (int attempt = 0; attempt < 3 && replies.Count < 32; attempt++)
        {
            await udp.SendAsync(query, target, cancellationToken);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            window.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                while (replies.Count < 32)
                {
                    var reply = await udp.ReceiveAsync(window.Token);
                    if (!reply.RemoteEndPoint.Equals(target) || reply.Buffer.Length > 16384) continue;
                    replies.Add(new(reply.RemoteEndPoint.ToString(), Convert.ToHexString(reply.Buffer), MatchesRecordedHeader(reply.Buffer)));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            catch (SocketException e) when (e.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionRefused)
            { /* ICMP port unreachable, for example while the local game is not running. */ }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(bound, target.ToString(), replies.ToArray());
    }
}
