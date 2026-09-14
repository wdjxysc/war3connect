using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace War3Connect.Agent;

// Read native Windows VERSIONINFO resources identically on Windows and Linux.
public static class PeVersion
{
    public static string Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var directory = pe.PEHeaders.PEHeader?.ResourceTableDirectory ?? throw new InvalidDataException("缺少 PE 资源。");
        if (directory.Size <= 0 || directory.Size > 16 * 1024 * 1024) throw new InvalidDataException("无效 PE 资源大小。");
        var data = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).ToArray();
        uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        ushort U16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        int Find(int offset, int? id)
        {
            int count = U16(offset + 12) + U16(offset + 14);
            for (int i = 0; i < count; i++)
            {
                int entry = checked(offset + 16 + i * 8);
                if (id == null || U32(entry) == id) return unchecked((int)U32(entry + 4));
            }
            throw new InvalidDataException("未找到游戏版本资源。");
        }
        // High bit marks a resource subdirectory; require the expected three levels.
        int version = Find(0, 16);
        if (version >= 0) throw new InvalidDataException("无效版本资源目录。");
        int name = Find(version & int.MaxValue, null);
        if (name >= 0) throw new InvalidDataException("无效版本名称目录。");
        int leaf = Find(name & int.MaxValue, null);
        if (leaf < 0) throw new InvalidDataException("无效版本语言资源。");
        int rva = checked((int)U32(leaf)), size = checked((int)U32(leaf + 4));
        if (size < 92 || size > 1024 * 1024) throw new InvalidDataException("无效版本资源长度。");
        var value = pe.GetSectionData(rva).GetContent(0, size).ToArray();
        var key = Encoding.Unicode.GetBytes("VS_VERSION_INFO\0");
        if (!value.AsSpan(6, key.Length).SequenceEqual(key) || BinaryPrimitives.ReadUInt16LittleEndian(value) > size || BinaryPrimitives.ReadUInt16LittleEndian(value.AsSpan(2)) < 52)
            throw new InvalidDataException("无效 VERSIONINFO。");
        int fixedOffset = (6 + key.Length + 3) & ~3;
        uint ReadFixed(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(value.AsSpan(fixedOffset + offset, 4));
        if (ReadFixed(0) != 0xFEEF04BD) throw new InvalidDataException("无效版本签名。");
        uint ms = ReadFixed(8), ls = ReadFixed(12);
        return $"{ms >> 16}.{ms & 65535}.{ls >> 16}.{ls & 65535}";
    }
}
