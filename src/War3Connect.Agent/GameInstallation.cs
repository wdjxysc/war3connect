using System.Diagnostics;
using War3Connect.Core;

namespace War3Connect.Agent;

public sealed record GameInstallation(string Executable, string Version)
{
    public static GameInstallation Inspect(string executable)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("war3.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择 War3 安装目录中的 war3.exe。");
        string exact = PeVersion.Read(executable);
        if (!Versions.IsSupported(exact)) throw new InvalidOperationException($"检测到版本 {exact}，本版只支持经典 TFT 1.27。");
        return new(executable, exact);
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
}
