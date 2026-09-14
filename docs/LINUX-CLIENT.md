# Linux 客户端（Avalonia）

Windows 和 Linux 共用 Avalonia 界面、账号和联机代理，连接同一个服务器。当前提供 x86_64 自包含包，无需另装 .NET；不包含 Wine 或 War3。

## 运行发布包

在具有图形桌面的 Linux 中解压并启动：

```bash
mkdir -p ~/Applications/war3connect
tar -xzf War3Connect-client-linux-x64-0.2.0.tar.gz -C ~/Applications/war3connect
cd ~/Applications/war3connect
chmod +x War3Connect.Client
./War3Connect.Client
```

客户端需要 X11 或桌面提供的 XWayland，以及 fontconfig 和中文字体。Ubuntu/Debian 可安装图形依赖：

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1 libxcb1 libxext6 libxrender1 fonts-noto-cjk
```

纯 SSH 服务器没有图形桌面，不能直接打开客户端窗口。`--smoke-test /绝对路径/preview.png` 仅用于无桌面的界面自检。

## 启动 War3 1.27

1. 安装发行版适用的 Wine 和 32 位游戏支持，先确认所用 Wine 环境可以启动经典 TFT 1.27。
2. 客户端选择 Linux 文件系统中的 `war3.exe`，例如 `/home/user/Games/Warcraft III/war3.exe`。不要输入 `C:\...` 路径。
3. 地图放在该游戏目录的 `Maps` 内。双方必须使用相同完整游戏版本和地图文件。
4. Linux 界面中的 Wine 程序默认 `wine`，也可填写 Wine 可执行文件的完整路径；不要在此填写参数或 shell 命令。
5. 如使用独立 Wine 环境，WINEPREFIX 填写该环境的绝对路径；留空继承启动客户端时的环境变量，未设置时使用 Wine 默认环境。
6. 点击“启动 War3”，进入冰封王座的局域网；游戏端口使用 6112，对战期间保持客户端运行。

Wine 设置随服务器、用户名和游戏路径一起保存，密码不保存。Linux 设置一般位于 `~/.local/share/War3Connect/settings.json`（遵循 .NET LocalApplicationData / XDG_DATA_HOME）；Windows 继续使用 `%LOCALAPPDATA%/War3Connect/settings.json`。

地图公告中的 Windows 路径按大小写不敏感逐段解析；存在仅大小写不同的重名文件或地图路径经过符号链接时拒绝使用，避免选中错误文件。

## 从源码构建

安装 .NET 8 SDK 后，在仓库根目录运行：

```bash
bash scripts/build.sh --test
bash scripts/start-client.sh
bash scripts/package-client.sh linux-x64
```

Windows 继续使用 `scripts/build.ps1 -Test`、`scripts/start-client.ps1`；`scripts/package.ps1` 同时生成 Windows x64、Linux x64 客户端和服务端包。

## 验证范围

自动测试覆盖版本资源读取、地图校验、启动参数、账号、房间、聊天、UDP 发现及 TCP/WebSocket 中继。界面自检验证窗口渲染和初始控件状态。

Wine 中的实际 War3 启动、Linux/Windows 互联及完整对局仍需要真实游戏验收；构建和模拟协议测试不能替代实机对局。
