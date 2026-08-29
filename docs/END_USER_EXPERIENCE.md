# End-user experience

## Goal

The normal user flow should be:

**Download → install → launch → emulator list appears → Update All**

No terminal, JSON, PowerShell, or SDK interaction is expected.

## Simple mode

Top-level navigation:
- Home
- Emulators
- Updates
- Backups
- Settings

## Error wording

Normal UI:
> Dolphin's update server could not be reached. Your existing installation was not changed.

Advanced log:
> HttpRequestException: 403 Forbidden...

## Recovery

Updates should prefer:
1. Verify download.
2. Backup existing emulator.
3. Install into the existing profile.
4. Validate expected executable.
5. Mark successful.
6. If validation fails, restore the backup automatically.

## Diagnostics

The support button creates `AutoEmulatorUpdate-Diagnostic-YYYYMMDD_HHMMSS.zip`.

It contains application/system/version metadata and recent logs. It must never package ROMs, saves, BIOS, firmware, keys, or personal files.
