#!/usr/bin/env bash
set -euo pipefail
for rid in win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64; do
  dotnet publish src/AutoEmulatorUpdate.App -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "artifacts/$rid"
done
