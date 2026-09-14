$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
& "$PSScriptRoot/build.ps1"
$clientOutput = Join-Path $projectRoot 'artifacts/release/client'
$serverOutput = Join-Path $projectRoot 'artifacts/release/server'
dotnet publish src/War3Connect.Client -c Release --no-restore --no-self-contained -o $clientOutput
if ($LASTEXITCODE -ne 0) { throw '客户端发布失败。' }
dotnet publish src/War3Connect.Server -c Release --no-restore --no-self-contained -p:UseAppHost=false -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw '服务端发布失败。' }
Copy-Item -LiteralPath README.md -Destination $clientOutput
Copy-Item -LiteralPath README.md -Destination $serverOutput
Copy-Item -LiteralPath docs -Destination $clientOutput -Recurse -Force
Copy-Item -LiteralPath docs -Destination $serverOutput -Recurse -Force
Compress-Archive -Path "$clientOutput/*" -DestinationPath artifacts/release/War3Connect-client-0.1.0.zip -Force
Compress-Archive -Path "$serverOutput/*" -DestinationPath artifacts/release/War3Connect-server-0.1.0.zip -Force
Write-Output "发布文件位于 $projectRoot/artifacts/release"
