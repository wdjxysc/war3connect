using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace War3Connect.Tests;

internal static class Fixtures
{
    // Independent encoder based on W3GS wire layout, not production parsing helpers.
    public static byte[] Announcement(ushort port = 6112, string map = "Maps\\fixture.w3x")
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(new byte[] { 0xf7, 0x30, 0, 0 });
        w.Write("PX3W"u8);
        w.Write(27u);
        w.Write(0x12345678u);
        w.Write(0x87654321u);
        w.Write(Encoding.UTF8.GetBytes("Test Game\0\0"));
        byte[] stats = new byte[13].Concat(Encoding.UTF8.GetBytes(map + "\0Host\0\0")).Concat(new byte[20]).ToArray();
        for (int i = 0; i < stats.Length; i += 7)
        {
            byte mask = 1;
            var block = stats.Skip(i).Take(7).ToArray();
            for (int j = 0; j < block.Length; j++)
                if ((block[j] & 1) == 1) mask |= (byte)(1 << (j + 1));
                else block[j]++;
            w.Write(mask);
            w.Write(block);
        }
        w.Write((byte)0);
        w.Write(8u); w.Write(9u); w.Write(1u); w.Write(7u); w.Write(100u); w.Write(port);
        var result = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)result.Length);
        return result;
    }
}

internal sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
}

internal sealed class FakeGame : IAsyncDisposable
{
    private readonly UdpClient _udp = new(new IPEndPoint(IPAddress.Loopback, 6112));
    private readonly TcpListener _tcp = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _echoes = [];
    private readonly Task _discover, _accept;
    public volatile bool Advertising = true;
    public byte[]? LastProxyAnnouncement;
    public ushort Port { get; }
    public FakeGame()
    {
        _tcp.Start();
        Port = (ushort)((IPEndPoint)_tcp.LocalEndpoint).Port;
        _discover = Discover();
        _accept = Accept();
    }
    private async Task Discover()
    {
        try
        {
            while (true)
            {
                var received = await _udp.ReceiveAsync(_stop.Token);
                if (received.Buffer.Length >= 4 && received.Buffer[1] == 0x2f && Advertising)
                    await _udp.SendAsync(Fixtures.Announcement(Port), received.RemoteEndPoint, _stop.Token);
                if (received.Buffer.Length >= 4 && received.Buffer[1] == 0x30) LastProxyAnnouncement = received.Buffer;
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task Accept()
    {
        try { while (true) _echoes.Add(Echo(await _tcp.AcceptTcpClientAsync(_stop.Token))); }
        catch (OperationCanceledException) { }
    }
    private async Task Echo(TcpClient client)
    {
        using (client)
        {
            try
            {
                var buffer = new byte[713]; // Deliberately changes stream chunk boundaries.
                var stream = client.GetStream();
                while (true)
                {
                    int n = await stream.ReadAsync(buffer, _stop.Token);
                    if (n == 0) return;
                    await stream.WriteAsync(buffer.AsMemory(0, n), _stop.Token);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        await Task.WhenAll(_accept, _discover);
        await Task.WhenAll(_echoes);
        _tcp.Stop(); _udp.Dispose(); _stop.Dispose();
    }
}
