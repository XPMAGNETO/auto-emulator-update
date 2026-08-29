#define MyAppName "Auto Emulator Updater"
#define MyAppVersion "10.1.0-alpha.7"
#define MyAppPublisher "Auto Emulator Updater"
#define MyAppExeName "AutoEmulatorUpdate.App.exe"

[Setup]
AppId={{C137E5C8-1F44-4C97-99A1-7A720B73AFC9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Auto Emulator Updater
DefaultGroupName=Auto Emulator Updater
DisableProgramGroupPage=yes
OutputBaseFilename=AutoEmulatorUpdate-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible arm64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

[Files]
Source: "..\..\artifacts\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Auto Emulator Updater"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0
Name: "{autodesktop}\Auto Emulator Updater"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; IconIndex: 0; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Auto Emulator Updater"; Flags: nowait postinstall skipifsilent
