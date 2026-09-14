$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$clientExe = Join-Path $projectRoot 'src/War3Connect.Client/bin/Release/net8.0/War3Connect.Client.exe'
if (-not (Test-Path -LiteralPath $clientExe)) { & "$PSScriptRoot/build.ps1" }
Start-Process -FilePath $clientExe -WorkingDirectory (Split-Path -Parent $clientExe)
