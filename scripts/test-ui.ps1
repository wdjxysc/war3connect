$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$clientPath = Join-Path $projectRoot 'src/War3Connect.Client/bin/Release/net8.0-windows/War3Connect.Client.exe'
$previewPath = Join-Path $projectRoot 'artifacts/client-preview.png'
if (-not (Test-Path -LiteralPath $clientPath)) { throw '请先构建客户端。' }
$previewProcess = Start-Process -FilePath $clientPath -ArgumentList @('--smoke-test', ('"' + $previewPath + '"')) -WindowStyle Hidden -PassThru
if (-not $previewProcess.WaitForExit(20000)) {
    Stop-Process -Id $previewProcess.Id
    throw '界面自检超时。'
}
if ($previewProcess.ExitCode -ne 0) { throw "界面自检失败，退出码 $($previewProcess.ExitCode)。" }
Write-Output "界面自检通过：$previewPath"
