#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_HOME="$PWD/artifacts/dotnet-home"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet restore War3Connect.sln --configfile NuGet.Config
dotnet build War3Connect.sln -c Release --no-restore
if [[ "${1:-}" == "--test" ]]; then
    dotnet run --project tests/War3Connect.Tests -c Release --no-build
    dotnet src/War3Connect.Client/bin/Release/net8.0/War3Connect.Client.dll --smoke-test "$PWD/artifacts/client-preview-linux.png"
fi
