using System.Buffers.Binary;
using System.Text;

namespace War3Connect.Core;

public sealed record LanGame(byte[] Packet, uint HostCounter, uint EntryKey, string Name, string MapPath, ushort Port);

/// <summary>W3GS TFT 1.27 discovery. Retains the original host counter, entry key and encoded map data.</summary>
public static class LanProtocol
{
    public const int DiscoveryPort = 6112;
    public static byte[] Search()
    {
        byte[] p = [0xf7, 0x2f, 16, 0, 0x50, 0x58, 0x33, 0x57, 27, 0, 0, 0, 0, 0, 0, 0];
        return p;
    }

    public static LanGame Parse(byte[] packet)
    {
        if (packet.Length is < 47 or > 4096 || packet[0] != 0xf7 || packet[1] != 0x30
            || BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(2)) != packet.Length
            || !packet.AsSpan(4, 4).SequenceEqual("PX3W"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8)) != 27)
            throw new InvalidDataException("不是有效的 War3 TFT 1.27 游戏公告。");
        int offset = 20;
        var name = ReadString(packet, ref offset);
        _ = ReadString(packet, ref offset); // Empty password string.
        int statStart = offset;
        _ = ReadString(packet, ref offset);
        if (offset + 22 != packet.Length || name.Length == 0)
            throw new InvalidDataException("游戏公告长度或名称无效。");
        var stat = DecodeStat(packet.AsSpan(statStart, offset - statStart - 1));
        int mapOffset = 13;
        if (stat.Length < 15) throw new InvalidDataException("地图信息缺失。");
        var map = ReadString(stat, ref mapOffset);
        var port = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(packet.Length - 2));
        if (port == 0 || string.IsNullOrWhiteSpace(map)) throw new InvalidDataException("游戏端口或地图无效。");
        return new(packet.ToArray(), BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16)), name, map, port);
    }

    public static byte[] WithPort(LanGame game, ushort port)
    {
        var copy = game.Packet.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(copy.Length - 2), port);
        return copy;
    }

    public static byte[] Remove(uint counter)
    {
        byte[] packet = [0xf7, 0x33, 8, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), counter);
        return packet;
    }

    private static string ReadString(byte[] bytes, ref int offset)
    {
        if (offset < 0 || offset >= bytes.Length) throw new InvalidDataException("公告字符串不完整。");
        int end = Array.IndexOf(bytes, (byte)0, offset);
        if (end < 0) throw new InvalidDataException("公告字符串未结束。");
        string value = Encoding.UTF8.GetString(bytes, offset, end - offset);
        offset = end + 1;
        return value;
    }

    private static byte[] DecodeStat(ReadOnlySpan<byte> encoded)
    {
        var decoded = new List<byte>();
        byte mask = 0;
        for (int i = 0; i < encoded.Length; i++)
        {
            if ((encoded[i] & 1) == 0) throw new InvalidDataException("地图编码无效。");
            if (i % 8 == 0) mask = encoded[i];
            else decoded.Add((byte)(encoded[i] - ((mask & (1 << (i % 8))) == 0 ? 1 : 0)));
        }
        return decoded.ToArray();
    }
}
