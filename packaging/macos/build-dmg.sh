#!/usr/bin/env bash
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PUBLISH="$ROOT/artifacts/$RID"
VERSION="$(cat "$ROOT/VERSION")"
APP="$ROOT/artifacts/Auto Emulator Update.app"
DMG="$ROOT/artifacts/AutoEmulatorUpdate-${VERSION}-${RID}.dmg"

rm -rf "$APP" "$DMG"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# Move the publish payload instead of copying it. GitHub macOS runners have
# limited free disk space and a second full self-contained runtime copy can
# make hdiutil fail with ENOSPC, especially for the Intel build.
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

# Once the .app exists, compiler outputs and NuGet caches are disposable.
# Clearing them leaves hdiutil enough temporary workspace on hosted runners.
rm -rf "$ROOT/src/AutoEmulatorUpdate.App/bin" "$ROOT/src/AutoEmulatorUpdate.App/obj" \
       "$ROOT/src/AutoEmulatorUpdate.Core/bin" "$ROOT/src/AutoEmulatorUpdate.Core/obj" || true
dotnet nuget locals all --clear >/dev/null 2>&1 || true

df -h .
hdiutil create -volname "Auto Emulator Update" -srcfolder "$APP" -ov -format UDZO "$DMG"
echo "$DMG"
