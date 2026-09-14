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
        string exact = PeVersion.Read(executable);
        if (!Versions.IsSupported(exact)) throw new InvalidOperationException($"检测到版本 {exact}，本版只支持经典 TFT 1.27。");
        if (!File.Exists(mapFile) || !(Path.GetExtension(mapFile).Equals(".w3x", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(mapFile).Equals(".w3m", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("请选择已安装的 .w3x 或 .w3m 地图。");
        var root = Path.GetDirectoryName(executable)!;
        var resolvedMap = GamePaths.ResolveMap(root, Path.GetRelativePath(root, mapFile));
        if (resolvedMap == null) throw new InvalidOperationException("请将地图放入游戏安装目录的 Maps 文件夹后选择；不支持符号链接或仅大小写不同的重名文件。");
        mapFile = resolvedMap;
        using var stream = File.OpenRead(mapFile);
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        return new(executable, exact, mapFile, hash);
    }
    public ProcessStartInfo CreateLaunchInfo(string? wine = null, string? winePrefix = null)
    {
        var info = new ProcessStartInfo { WorkingDirectory = Path.GetDirectoryName(Executable)!, UseShellExecute = false };
        if (OperatingSystem.IsLinux())
        {
            info.FileName = string.IsNullOrWhiteSpace(wine) ? "wine" : wine.Trim();
            info.ArgumentList.Add(Executable);
            if (!string.IsNullOrWhiteSpace(winePrefix)) info.Environment["WINEPREFIX"] = Path.GetFullPath(winePrefix);
        }
        else info.FileName = Executable;
        info.ArgumentList.Add("-window");
        return info;
    }
    public void Launch(string? wine = null, string? winePrefix = null)
    {
        try { using var process = Process.Start(CreateLaunchInfo(wine, winePrefix)); }
        catch (System.ComponentModel.Win32Exception e) when (OperatingSystem.IsLinux())
        { throw new InvalidOperationException("无法启动 Wine，请安装 Wine 并检查 Wine 程序路径。", e); }
    }
    public async Task<bool> MatchesAdvertisement(LanGame game, CancellationToken ct)
    {
        string root = Path.GetDirectoryName(Executable)!;
        string? candidate = GamePaths.ResolveMap(root, game.MapPath);
        if (candidate == null) return false;
        using var stream = File.OpenRead(candidate);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) == MapHash;
    }
}
