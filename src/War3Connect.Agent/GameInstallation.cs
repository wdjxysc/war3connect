using System.Diagnostics;
using War3Connect.Core;

namespace War3Connect.Agent;

public sealed record GameInstallation(string Executable, string Version, string GameId = GameCatalog.War3)
{
    public static GameInstallation Inspect(string executable, string gameId = GameCatalog.War3)
    {
        if (!GameCatalog.IsKnown(gameId)) throw new InvalidOperationException("不支持的游戏。");
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals(GameCatalog.Executable(gameId), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"请选择游戏目录中的 {GameCatalog.Executable(gameId)}。");
        string exact = PeVersion.Read(executable);
        if (!GameCatalog.SupportsVersion(gameId, exact)) throw new InvalidOperationException($"检测到版本 {exact}，需要 {(gameId == GameCatalog.StarCraft ? "星际 1.16.1" : "经典 TFT 1.27")} 的完整版本号。");
        return new(executable, exact, gameId);
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
        if (GameId == GameCatalog.War3) info.ArgumentList.Add("-window");
        return info;
    }
    public void Launch(string? wine = null, string? winePrefix = null)
    {
        try { using var process = Process.Start(CreateLaunchInfo(wine, winePrefix)); }
        catch (System.ComponentModel.Win32Exception e) when (OperatingSystem.IsLinux())
        { throw new InvalidOperationException("无法启动 Wine，请安装 Wine 并检查 Wine 程序路径。", e); }
    }
}
