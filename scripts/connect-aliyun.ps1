param([int]$LocalPort = 15080)
$ErrorActionPreference = 'Stop'
if ($LocalPort -lt 1024 -or $LocalPort -gt 65535) { throw '本地端口需在 1024～65535 之间。' }
Write-Output "客户端服务器地址填写 http://127.0.0.1:$LocalPort"
Write-Output '保持此窗口运行；按 Ctrl+C 关闭加密隧道。需要服务器账号的 SSH 登录权限。'
ssh -o ExitOnForwardFailure=yes -o StrictHostKeyChecking=yes -o ServerAliveInterval=30 -o ServerAliveCountMax=3 -N -L "127.0.0.1:${LocalPort}:127.0.0.1:5080" deploy-user@203.0.113.10
if ($LASTEXITCODE -ne 0) { throw 'SSH 隧道未能建立或已经断开。' }
