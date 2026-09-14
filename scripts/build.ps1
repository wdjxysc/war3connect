param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot 'artifacts/dotnet-home'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
dotnet restore War3Connect.sln --configfile NuGet.Config
if ($LASTEXITCODE -ne 0) { throw '还原失败。' }
dotnet build War3Connect.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
if ($Test) {
    dotnet run --project tests/War3Connect.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
    & "$PSScriptRoot/test-ui.ps1"
}
