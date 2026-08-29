#!/usr/bin/env bash
set -euo pipefail

RID="${1:-linux-x64}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
VERSION="$(cat "$ROOT/VERSION")"
PKG="$ROOT/artifacts/deb-$RID"
rm -rf "$PKG"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/auto-emulator-update" "$PKG/usr/bin" "$PKG/usr/share/applications"
cp -a "$ROOT/artifacts/$RID/." "$PKG/opt/auto-emulator-update/"
cat > "$PKG/DEBIAN/control" <<EOF
Package: auto-emulator-update
Version: ${VERSION%%-*}
Section: games
Priority: optional
Architecture: amd64
Maintainer: Auto Emulator Update contributors
Description: Cross-platform emulator updater and manager
EOF
ln -s /opt/auto-emulator-update/AutoEmulatorUpdate.App "$PKG/usr/bin/auto-emulator-update"
cp "$ROOT/packaging/linux/auto-emulator-update.desktop" "$PKG/usr/share/applications/"
dpkg-deb --build "$PKG" "$ROOT/artifacts/AutoEmulatorUpdate-${VERSION}-${RID}.deb"
