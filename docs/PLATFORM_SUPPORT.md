# Platform support

Auto Emulator Update currently recognizes these desktop/gaming environments:

- Windows
- macOS
- Linux
- SteamOS / Steam Deck
- Pop!_OS
- CachyOS
- Batocera
- RetroBat portable Windows installations

## Batocera policy

Batocera's bundled emulator stack is treated as system-managed. Auto Emulator Update should not replace built-in Batocera emulator binaries. Standalone emulator installations created by Auto Emulator Update use writable `/userdata` paths.

## RetroBat policy

RetroBat is treated as a portable Windows frontend/distribution rather than a separate operating system. Detection scans fixed and removable Windows drives so USB/portable RetroBat installations can be imported.
