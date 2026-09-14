#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-linux-x64}"
case "$rid" in linux-x64|win-x64) ;; *) echo 'Supported targets: linux-x64, win-x64' >&2; exit 1;; esac
mkdir -p artifacts/release
# A fresh staging directory prevents stale private configuration from entering a release.
output="$(mktemp -d "$PWD/artifacts/release/stage-$rid-XXXXXXXX")"
dotnet publish src/War3Connect.Client -p:DebugType=None -p:DebugSymbols=false -c Release -r "$rid" --self-contained true -o "$output"
cp README.md "$output/"
mkdir -p "$output/docs"
cp docs/LINUX-CLIENT.md "$output/docs/"
if [[ "$rid" == linux-x64 ]]; then
    chmod +x "$output/War3Connect.Client"
    tar -czf "artifacts/release/War3Connect-client-$rid-0.3.0.tar.gz" -C "$output" .
fi
