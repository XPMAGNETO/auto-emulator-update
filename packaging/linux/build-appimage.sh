#!/usr/bin/env bash
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/$RID"
APPDIR="$ROOT/artifacts/AppDir-$RID"
OUT="$ROOT/artifacts/AutoEmulatorUpdate-${RID}.AppImage"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps" "$APPDIR/usr/share/metainfo"
cp -a "$PUBLISH/." "$APPDIR/usr/bin/"
cp "$ROOT/packaging/linux/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/AutoEmulatorUpdate.App"
cp "$ROOT/packaging/linux/auto-emulator-update.desktop" "$APPDIR/auto-emulator-update.desktop"
cp "$ROOT/packaging/linux/auto-emulator-update.desktop" "$APPDIR/usr/share/applications/"
cp "$ROOT/packaging/linux/auto-emulator-update.appdata.xml" "$APPDIR/usr/share/metainfo/"
if [[ -f "$ROOT/src/AutoEmulatorUpdate.App/Assets/app-icon.png" ]]; then
  cp "$ROOT/src/AutoEmulatorUpdate.App/Assets/app-icon.png" "$APPDIR/auto-emulator-update.png"
  cp "$ROOT/src/AutoEmulatorUpdate.App/Assets/app-icon.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/auto-emulator-update.png"
fi

APPIMAGETOOL="${APPIMAGETOOL:-appimagetool}"
ARCH="$(uname -m)" "$APPIMAGETOOL" "$APPDIR" "$OUT"
echo "$OUT"
