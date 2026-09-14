using System.Diagnostics;
using System.Security.Cryptography;
using War3Connect.Core;

namespace War3Connect.Agent;

public sealed record GameInstallation(string Executable, string Version, string MapFile, string MapHash)
{
    public static async Task<GameInstallation> Inspect(string executable, string mapFile)
    {
        executable = Path.GetFullPath(executable);
        mapFile = Path.GetFullPath(mapFile);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("war3.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择 War3 安装目录中的 war3.exe。");
        var version = FileVersionInfo.GetVersionInfo(executable);
        string exact = $"{version.FileMajorPart}.{version.FileMinorPart}.{version.FileBuildPart}.{version.FilePrivatePart}";
        if (!Versions.IsSupported(exact)) throw new InvalidOperationException($"检测到版本 {exact}，本版只支持经典 TFT 1.27。");
        if (!File.Exists(mapFile) || !(Path.GetExtension(mapFile).Equals(".w3x", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(mapFile).Equals(".w3m", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("请选择已安装的 .w3x 或 .w3m 地图。");
        var mapsRoot = Path.Combine(Path.GetDirectoryName(executable)!, "Maps") + Path.DirectorySeparatorChar;
        if (!mapFile.StartsWith(mapsRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请将地图放入游戏安装目录的 Maps 文件夹后选择。");
        using var stream = File.OpenRead(mapFile);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        return new(executable, exact, mapFile, hash);
    }
    public void Launch() => Process.Start(new ProcessStartInfo(Executable, "-window")
        { WorkingDirectory = Path.GetDirectoryName(Executable)!, UseShellExecute = true });
    public async Task<bool> MatchesAdvertisement(LanGame game, CancellationToken ct)
    {
        string root = Path.GetDirectoryName(Executable)!;
        string candidate = Path.GetFullPath(Path.Combine(root, game.MapPath.Replace('\\', Path.DirectorySeparatorChar)));
        string maps = Path.Combine(root, "Maps") + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(maps, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate)) return false;
        using var stream = File.OpenRead(candidate);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) == MapHash;
    }
}
