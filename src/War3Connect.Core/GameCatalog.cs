namespace War3Connect.Core;

public static class GameCatalog
{
    public const string War3 = "war3-tft";
    public const string StarCraft = "starcraft-bw";
    public static bool IsKnown(string? id) => id is War3 or StarCraft;
    public static string Name(string id) => id == StarCraft ? "星际争霸：母巢之战" : "魔兽争霸 III：冰封王座";
    public static string Executable(string id) => id == StarCraft ? "StarCraft.exe" : "war3.exe";
    public static int Capacity(string id) => id == StarCraft ? 8 : 12;
    public static bool SupportsVersion(string? id, string? version) => id switch
    {
        War3 => Versions.IsSupported(version),
        StarCraft => Version.TryParse(version, out var v) && v.Major == 1 && v.Minor == 16 && v.Build == 1 && v.Revision >= 0,
        _ => false
    };
}
