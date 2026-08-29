# v10.1 architecture

## Product layers

### Simple UX
The normal user sees Home, Emulators, Updates, Backups and Settings.

### Advanced UX
Health, raw logs, resolver/manifests and support diagnostics are separated from the primary workflow.

### Core updater
The update coordinator is UI-independent:
1. Resolve release.
2. Verify platform/package.
3. Check disk space.
4. Download/cache.
5. Verify checksum where available.
6. Backup.
7. Stage/extract.
8. Deploy.
9. Validate.
10. Roll back on failure.

### Platform abstraction
- Scheduling: Task Scheduler / systemd user timer / launchd.
- Packaging: Setup.exe / AppImage + DEB / DMG.
- Notifications are routed by the platform notification service.
- Discovery uses platform-aware default search roots.

### Manifest system
Emulator support lives in JSON definitions. User/community definitions can be added without changing the updater core.

### Self-update
The GitHub setup script patches the repository owner into `BuildInfo.cs`. Release builds then query that repository's GitHub Releases feed.
