# Auto Emulator Update

A cross-platform desktop app that finds installed emulators, checks for new versions, backs up the current installation, installs updates, validates the result, and can automatically restore the previous version if an update fails.

## For normal users

You should not need PowerShell, a terminal, JSON files, or the .NET SDK.

On the GitHub **Releases** page download:

- **Windows:** `AutoEmulatorUpdate-...-Windows-Setup.exe`
- **RetroBat:** use the Windows installer; RetroBat is detected as a Windows frontend, including portable installs on USB/removable drives.
- **SteamOS / Steam Deck:** `AutoEmulatorUpdate-...-SteamOS-x64.AppImage`
- **Pop!_OS:** use the Linux x64 AppImage or `.deb`.
- **CachyOS:** use the Linux x64 AppImage; Auto Emulator Update detects CachyOS separately while using compatible Linux emulator packages.
- **Batocera:** detected separately. Built-in Batocera emulators remain Batocera-managed; Auto Emulator Update only manages standalone emulators stored in writable `/userdata` locations.
- **Other Linux:** `AutoEmulatorUpdate-...-Linux-x64.AppImage` or the `.deb` where appropriate.
- **macOS:** the `.dmg` matching Intel (`osx-x64`) or Apple Silicon (`osx-arm64`).

After the first install, the app can check for newer Auto Emulator Update releases itself.

## Linux distribution support

Auto Emulator Update reads `/etc/os-release` and recognizes SteamOS, Pop!_OS, CachyOS and Batocera as distinct environments while continuing to use Linux-compatible emulator manifests and packages.

### SteamOS / Steam Deck

SteamOS uses writable user-space locations and removable/microSD storage. Auto Emulator Update looks in user Applications/Emulators folders, Flatpak export/config locations and `/run/media`. The dedicated SteamOS AppImage does not require disabling the SteamOS read-only system image.

### Pop!_OS

Pop!_OS uses the normal Linux update path. The application identifies Pop!_OS in diagnostics and uses the standard Linux AppImage/DEB-compatible workflow.

### CachyOS

CachyOS uses the normal Linux update path while being identified separately in diagnostics. Auto Emulator Update does not enable or modify CachyOS/Arch repositories; emulator packages continue to come from each emulator project's supported update source.

### Batocera

Batocera manages its bundled emulator stack through Batocera's own system updater. Auto Emulator Update deliberately does not replace those built-in binaries. Standalone emulator installs created by Auto Emulator Update go under:

`/userdata/system/auto-emulator-update/emulators`

Discovery is restricted to writable `/userdata` locations on Batocera so system-managed emulator files remain untouched.

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

The importer can discover emulator executable paths from supported frontend configurations, including LaunchBox, RetroBat, ES-DE, Pegasus and Playnite-style configurations.

RetroBat is officially a Windows frontend rather than a separate operating system. Auto Emulator Update scans both fixed and removable Windows drives for portable RetroBat installations.

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
