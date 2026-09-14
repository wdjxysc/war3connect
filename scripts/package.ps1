$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
& "$PSScriptRoot/build.ps1"
function Reset-PublishDirectory([string]$Path) {
    $releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/release'))
    $targetPath = [IO.Path]::GetFullPath($Path)
    if (-not $targetPath.StartsWith($releaseRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw '发布目录超出允许范围。' }
    foreach ($ancestor in @((Join-Path $projectRoot 'artifacts'), $releaseRoot, $targetPath)) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '发布目录不能是符号链接。' }
    }
    if (Test-Path -LiteralPath $targetPath) { Remove-Item -LiteralPath $targetPath -Recurse -Force }
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}
$serverOutput = Join-Path $projectRoot 'artifacts/release/server'
foreach ($rid in @('win-x64', 'linux-x64')) {
    $clientOutput = Join-Path $projectRoot "artifacts/release/client-$rid"
    Reset-PublishDirectory $clientOutput
    dotnet publish src/War3Connect.Client -p:DebugType=None -p:DebugSymbols=false -c Release -r $rid --self-contained true -o $clientOutput
    if ($LASTEXITCODE -ne 0) { throw "$rid 客户端发布失败。" }
    Copy-Item -LiteralPath README.md -Destination $clientOutput
    New-Item -ItemType Directory -Path "$clientOutput/docs" -Force | Out-Null
    Copy-Item -LiteralPath docs/LINUX-CLIENT.md -Destination "$clientOutput/docs"
    if ($rid -eq 'win-x64') {
        Compress-Archive -Path "$clientOutput/*" -DestinationPath "artifacts/release/War3Connect-client-$rid-0.3.0.zip" -Force
    } else {
        tar -czf "artifacts/release/War3Connect-client-$rid-0.3.0.tar.gz" -C $clientOutput .
        if ($LASTEXITCODE -ne 0) { throw 'Linux 打包失败。' }
    }
}
Reset-PublishDirectory $serverOutput
dotnet publish src/War3Connect.Server -p:DebugType=None -p:DebugSymbols=false -c Release --no-restore --no-self-contained -p:UseAppHost=false -o $serverOutput
if ($LASTEXITCODE -ne 0) { throw '服务端发布失败。' }
Copy-Item -LiteralPath README.md -Destination $serverOutput
New-Item -ItemType Directory -Path "$serverOutput/docs" -Force | Out-Null
Copy-Item -LiteralPath docs/DEPLOYMENT.md, docs/API.md -Destination "$serverOutput/docs"
Compress-Archive -Path "$serverOutput/*" -DestinationPath artifacts/release/War3Connect-server-0.3.0.zip -Force
Write-Output "发布文件位于 $projectRoot/artifacts/release"
