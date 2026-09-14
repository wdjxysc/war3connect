param([string]$Url = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$serverDll = Join-Path $projectRoot 'src/War3Connect.Server/bin/Release/net8.0/War3Connect.Server.dll'
if (-not (Test-Path -LiteralPath $serverDll)) { & "$PSScriptRoot/build.ps1" }
dotnet $serverDll --urls $Url --contentRoot (Join-Path $projectRoot 'src/War3Connect.Server') --DataDirectory (Join-Path $projectRoot 'data')
