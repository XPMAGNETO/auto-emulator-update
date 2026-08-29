# Auto Emulator Update v10.1.0-alpha.2

This prerelease expands platform support and improves release packaging.

## Highlights

- SteamOS / Steam Deck detection and dedicated AppImage asset.
- Pop!_OS detection with Linux-compatible package handling.
- CachyOS detection with Linux-compatible package handling.
- Batocera-safe mode that does not replace Batocera-managed emulator binaries; standalone installs use writable `/userdata` storage.
- Improved portable RetroBat discovery, including removable Windows drives.
- Cross-platform CI validated on Windows, Linux, and macOS.
- macOS packaging updated to reduce temporary disk usage during DMG creation.

This remains an alpha release. Backups and automatic rollback are recommended while emulator manifests continue to be verified platform by platform.
