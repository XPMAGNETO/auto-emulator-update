# Auto Emulator Update

A cross-platform desktop app that finds installed emulators, checks for new versions, backs up the current installation, installs updates, validates the result, and can automatically restore the previous version if an update fails.

## For normal users

You should not need PowerShell, a terminal, JSON files, or the .NET SDK.

On the GitHub **Releases** page download:

- **Windows:** `AutoEmulatorUpdate-...-Windows-Setup.exe`
- **SteamOS / Steam Deck:** `AutoEmulatorUpdate-...-SteamOS-x64.AppImage`
- **Linux:** `AutoEmulatorUpdate-...-Linux-x64.AppImage` or the `.deb`
- **macOS:** the `.dmg` matching Intel (`osx-x64`) or Apple Silicon (`osx-arm64`)

After the first install, the app can check for newer Auto Emulator Update releases itself.

## SteamOS / Steam Deck

SteamOS is detected separately from generic Linux while reusing Linux-compatible emulator packages. Auto Emulator Update looks in SteamOS-friendly writable locations such as the user's Applications/Emulators folders, Flatpak export/config locations, and `/run/media` for removable or microSD storage.

The SteamOS build is distributed as an AppImage so Auto Emulator Update does not need to disable the SteamOS read-only system image or install files with `pacman`. Run it from Desktop Mode; it can then be added to Steam as a Non-Steam application for access from Gaming Mode.

## First launch

The setup wizard asks only four things:

1. Find my emulators.
2. Choose safe automatic updates or ask/check-only mode.
3. Choose startup/scheduled maintenance.
4. Finish.

The recommended update flow is:

**Download → verify → backup → install → validate → automatically rollback if validation fails**

## Main screen

The normal interface has only:

- **Home**
- **Emulators**
- **Updates**
- **Backups**
- **Settings**

Technical health/resolver information is under **Settings → Advanced / Developer Tools**.

## Emulator Library

The **Available to install** tab lets the user search supported emulators and install the correct package for the current operating system/CPU when that manifest has a verified package rule.

## Frontends

The importer can discover emulator executable paths from supported frontend configurations, including the existing LaunchBox / RetroBat / ES-DE / Pegasus / Playnite-style configuration framework.

If a frontend manages its own emulator updates, Auto Emulator Update can leave that install frontend-managed.

## Safety

Auto Emulator Update does **not** download copyrighted console BIOS, firmware, keys, ROMs, or system files.

Diagnostic ZIPs intentionally exclude:
- ROMs
- saves
- BIOS/firmware/key files
- personal documents
- emulator installation paths from the public summary

## Developers

v10.1 is written in C#/.NET 10 with Avalonia.

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/AutoEmulatorUpdate.App
```

Emulator definitions are JSON files in `manifests/emulators`.

## Publishing your GitHub repository

After installing/authenticating GitHub CLI:

Windows:

```powershell
.\scripts\create-github-repo.ps1
```

Linux/macOS:

```bash
./scripts/create-github-repo.sh
```

The setup script automatically writes your GitHub username into the application's self-update repository setting before the first push.

Tagging a release then builds the end-user packages:

```bash
git tag v10.1.0-alpha.1
git push origin v10.1.0-alpha.1
```

GitHub Actions creates Windows Setup.exe, Linux/SteamOS AppImages, Linux DEB, and macOS DMGs.
