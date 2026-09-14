param([string]$TargetAddress = '127.0.0.1', [ValidateRange(0,65535)][int]$SourcePort = 0)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $projectRoot 'src/War3Connect.Client/bin/Release/net8.0/War3Connect.Client.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw '请先构建客户端。' }
$outputDirectory = Join-Path $projectRoot 'artifacts/starcraft-probes'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$report = Join-Path $outputDirectory ('probe-' + [Guid]::NewGuid().ToString('N') + '.json')
dotnet $dll --probe-starcraft $TargetAddress $SourcePort $report
if ($LASTEXITCODE -ne 0) { throw '探测失败。固定源端口如已被游戏占用，请改用随机端口或另一台电脑。' }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
Write-Output "探测：$($result.LocalEndpoint) → $($result.Target)，回复 $($result.Replies.Count) 个。"
Write-Output "报告：$report"
Write-Output '收到报文不代表入房成功；没有回复也不能单独证明查询方案不可行。'
