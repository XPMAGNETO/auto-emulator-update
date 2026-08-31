# Mobile Companion

The Android and iOS applications are companion controllers for Auto Emulator Updater running on a desktop computer. Phones do not directly modify desktop emulator installations.

## First milestone

- Shared Avalonia mobile interface for Android, iPhone, and iPad.
- HTTPS-only desktop pairing outside local development.
- One-time pairing code exchanged for a scoped bearer token.
- Status dashboard, emulator versions, update availability, commands, progress, and activity.
- Separate mobile CI so desktop release gates remain independent.

## Desktop API contract

The mobile client expects these endpoints under the paired desktop address:

- `POST /api/companion/pair`
- `GET /api/companion/status`
- `POST /api/companion/commands`

Supported initial command names are `check-all` and `update-all`. The desktop host, pairing-code screen, token persistence, discovery, and revocation controls are the next implementation milestone.

## Signing and distribution

Android release builds require a private keystore. iOS device and App Store builds require an Apple Developer account, signing certificate, bundle identifier, and provisioning profile. Signing secrets must be stored in GitHub Actions secrets and never committed.
