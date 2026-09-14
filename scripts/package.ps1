$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
& "$PSScriptRoot/build.ps1"
$serverOutput = Join-Path $projectRoot 'artifacts/release/server'
foreach ($rid in @('win-x64', 'linux-x64')) {
    $clientOutput = Join-Path $projectRoot "artifacts/release/client-$rid"
    dotnet publish src/War3Connect.Client -c Release -r $rid --self-contained true -o $clientOutput
    if ($LASTEXITCODE -ne 0) { throw "$rid 客户端发布失败。" }
    Copy-Item -LiteralPath README.md -Destination $clientOutput
    Copy-Item -LiteralPath docs -Destination $clientOutput -Recurse -Force
    if ($rid -eq 'win-x64') {
        Compress-Archive -Path "$clientOutput/*" -DestinationPath "artifacts/release/War3Connect-client-$rid-0.2.0.zip" -Force
    } else {
        tar -czf "artifacts/release/War3Connect-client-$rid-0.2.0.tar.gz" -C $clientOutput .
        if ($LASTEXITCODE -ne 0) { throw 'Linux 打包失败。' }
    }
}
dotnet publish src/War3Connect.Server -c Release --no-restore --no-self-contained -p:UseAppHost=false -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw '服务端发布失败。' }
Copy-Item -LiteralPath README.md -Destination $serverOutput
Copy-Item -LiteralPath docs -Destination $serverOutput -Recurse -Force
Compress-Archive -Path "$serverOutput/*" -DestinationPath artifacts/release/War3Connect-server-0.1.0.zip -Force
Write-Output "发布文件位于 $projectRoot/artifacts/release"
