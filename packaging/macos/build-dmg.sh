#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/$RID"
VERSION="$(cat "$ROOT/VERSION")"
APP="$ROOT/artifacts/Auto Emulator Update.app"
DMG="$ROOT/artifacts/AutoEmulatorUpdate-${VERSION}-${RID}.dmg"
RW_DMG="$ROOT/artifacts/AutoEmulatorUpdate-${RID}.rw.dmg"
MOUNT="$ROOT/artifacts/dmg-mount-${RID}"

rm -rf "$APP" "$DMG" "$RW_DMG" "$MOUNT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Move the publish payload instead of copying it to avoid duplicating the
# self-contained runtime on GitHub's hosted macOS runners.
find "$PUBLISH" -mindepth 1 -maxdepth 1 -exec mv {} "$APP/Contents/MacOS/" \;
rmdir "$PUBLISH" || true
chmod +x "$APP/Contents/MacOS/AutoEmulatorUpdate.App"

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleName</key><string>Auto Emulator Update</string>
<key>CFBundleDisplayName</key><string>Auto Emulator Update</string>
<key>CFBundleIdentifier</key><string>com.autoemulatorupdate.app</string>
<key>CFBundleVersion</key><string>${VERSION}</string>
<key>CFBundleShortVersionString</key><string>${VERSION}</string>
<key>CFBundleExecutable</key><string>AutoEmulatorUpdate.App</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
EOF

# Free disposable build caches before creating the image.
rm -rf "$ROOT/src/AutoEmulatorUpdate.App/bin" "$ROOT/src/AutoEmulatorUpdate.App/obj" \
       "$ROOT/src/AutoEmulatorUpdate.Core/bin" "$ROOT/src/AutoEmulatorUpdate.Core/obj" || true
dotnet nuget locals all --clear >/dev/null 2>&1 || true

# hdiutil's -srcfolder auto-sizing has produced ENOSPC on hosted runners even
# with ample host disk space. Build a deliberately oversized writable image,
# copy the .app into it, then convert that image to compressed UDZO.
APP_MB="$(du -sm "$APP" | awk '{print $1}')"
IMAGE_MB=$((APP_MB + 512))
if (( IMAGE_MB < 1024 )); then IMAGE_MB=1024; fi

mkdir -p "$MOUNT"
hdiutil create -size "${IMAGE_MB}m" -fs HFS+ -volname "Auto Emulator Update" -format UDRW "$RW_DMG"
hdiutil attach "$RW_DMG" -nobrowse -mountpoint "$MOUNT"
cleanup() {
  hdiutil detach "$MOUNT" >/dev/null 2>&1 || true
}
trap cleanup EXIT

ditto "$APP" "$MOUNT/Auto Emulator Update.app"
sync
hdiutil detach "$MOUNT"
trap - EXIT
rmdir "$MOUNT" || true

hdiutil convert "$RW_DMG" -format UDZO -o "$DMG"
rm -f "$RW_DMG"

echo "$DMG"
