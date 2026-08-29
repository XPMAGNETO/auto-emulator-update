#define MyAppName "Auto Emulator Update"
#define MyAppVersion "10.1.0-alpha.3"
#define MyAppPublisher "Auto Emulator Update"
#define MyAppExeName "AutoEmulatorUpdate.App.exe"

[Setup]
AppId={{C137E5C8-1F44-4C97-99A1-7A720B73AFC9}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Auto Emulator Update
DefaultGroupName=Auto Emulator Update
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
Name: "{autoprograms}\Auto Emulator Update"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Auto Emulator Update"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Auto Emulator Update"; Flags: nowait postinstall skipifsilent
