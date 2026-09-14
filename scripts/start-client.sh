#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project src/War3Connect.Client -c Release
