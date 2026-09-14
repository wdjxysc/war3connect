# 通用部署指南

公开仓库不保存实际公网地址、SSH 登录账号、内部站点或现场运维记录。下文的 `play.example.com` 和 `deploy-user` 仅为示例，使用时替换为自己的配置。

## 服务端

安装 ASP.NET Core 8 Runtime，将服务端发布包解压到专用部署目录。账号数据单独保存，并限制文件访问权限：

```bash
dotnet War3Connect.Server.dll --urls http://127.0.0.1:5080 --DataDirectory /path/to/private/data
```

使用 systemd 或其他进程管理器维护服务，前置 Caddy/Nginx 提供可信 HTTPS 和 WebSocket 转发。配置示例见 `deploy/Caddyfile.example`。不要将反向代理管理接口暴露到公网。

## 客户端配置

客户端默认连接本机开发地址。用户可在界面输入管理员提供的 HTTPS 地址并保存；无证书 HTTP 地址需勾选对应开关；该地址保存到用户配置，不写回源码。

如需定制默认地址，可私下修改发布目录中的 `clientsettings.json`。这种定制包会包含地址，不要再作为公开发行包上传。公网 IP 无法对实际连接它的玩家保密。

## SSH 排错

```powershell
./scripts/connect-server.ps1 -SshTarget deploy-user@play.example.com
```

隧道仅监听本机，使用者需已有 SSH 权限。保持进程运行，在客户端填写 `http://127.0.0.1:15080`。

## 可选：公网 IPv4 证书路由工具

`deploy/reconcile-public-ip.py` 适用于已有 Caddy HTTPS 服务、Certbot 和指定目录结构的部署。它不是通用的一键安装脚本；使用前应检查并适配脚本中的目录和 Caddy 配置结构。

实际 IP 从 `WAR3CONNECT_PUBLIC_IP` 环境变量读取。使用配套用户级 systemd 服务时，将 `deploy/deployment.env.example` 私下复制到 `~/.config/war3connect/deployment.env`，填写实际 IP，并设置权限 600。服务配置中的脚本路径需与实际安装位置一致。

证书私钥、Caddy 配置备份、账号数据和部署环境文件只能保存在服务器或私有目录中，不得放入公开仓库或发布包。现场运维笔记可放在本地被忽略的 `.private/` 目录。

## 升级和验证

升级前备份账号数据，发布到新目录后切换版本并重启；重启会中断房间和对局。客户端与服务端须使用兼容的协议版本。

```powershell
./scripts/test-public-endpoint.ps1 -Address https://play.example.com
```

此验证会创建临时诊断账号并运行中继测试，结束后退出账号；随机密码不保存。


## 可选：无证书 HTTP/WS

Windows 和 Linux 均可直接运行：

```bash
dotnet War3Connect.Server.dll --urls http://0.0.0.0:5080 --DataDirectory ./data
```

这会监听所有 IPv4 网卡。仅向需要连接的网络开放防火墙或云安全组 TCP 5080。客户端填写 `http://服务器地址:5080`，勾选“允许 HTTP（无证书）”，保存后登录。不要把 `0.0.0.0` 作为客户端目标地址。

HTTP 模式不需要证书或反向代理，游戏中继自动使用 WS；密码、令牌和游戏数据不加密，仅适合可信网络或临时测试。HTTPS 模式仍正常校验证书，不会自动降级。

若需同时提供两种入口，可保留 HTTPS 反向代理，并单独开放 HTTP 监听端口。本项目不会自动修改已有服务器、防火墙或证书路由。

```powershell
./scripts/test-public-endpoint.ps1 -Address http://play.example.com:5080 -AllowInsecureHttp
```
