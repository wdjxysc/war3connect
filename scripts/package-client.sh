#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-linux-x64}"
case "$rid" in linux-x64|win-x64) ;; *) echo 'Supported targets: linux-x64, win-x64' >&2; exit 1;; esac
output="$PWD/artifacts/release/client-$rid"
dotnet publish src/War3Connect.Client -c Release -r "$rid" --self-contained true -o "$output"
cp README.md "$output/"
cp -R docs "$output/"
if [[ "$rid" == linux-x64 ]]; then
    chmod +x "$output/War3Connect.Client"
    tar -czf "artifacts/release/War3Connect-client-$rid-0.2.0.tar.gz" -C "$output" .
fi
