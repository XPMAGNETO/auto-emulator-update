# Auto Emulator Update

A cross-platform desktop app that finds installed emulators, checks for new versions, backs up the current installation, installs updates, validates the result, and can automatically restore the previous version if an update fails.

## For normal users

You should not need PowerShell, a terminal, JSON files, or the .NET SDK.

Planned GitHub Releases packages:

- Windows Setup.exe
- Linux AppImage / DEB
- macOS DMG for Intel and Apple Silicon

## Update safety

**Download → verify → backup → install → validate → automatically rollback if validation fails**

## Main interface

- Home
- Emulators
- Updates
- Backups
- Settings

Technical resolver and health information is kept under Advanced / Developer Tools.

## Safety

Auto Emulator Update does not download copyrighted console BIOS, firmware, keys, ROMs, or system files.

## Development

v10.1 is written in C#/.NET with Avalonia. Emulator definitions are stored under `manifests/emulators`.
