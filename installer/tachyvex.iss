#define MyAppName "NEOX"
#ifndef MyAppVersion
  #define MyAppVersion "2.0.0"
#endif
#define MyAppExe "Neox.exe"

[Setup]
; Stały AppId: kolejne wersje aktualizują zainstalowaną, zamiast instalować obok.
AppId={{7C2E9B14-3A58-4F0D-8B6E-1D95A0C4E7F3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=spinprint
AppPublisherURL=https://spinprint.pl
AppSupportURL=https://spinprint.pl
AppCopyright=© spinprint (spinprint.pl)
VersionInfoCompany=spinprint
VersionInfoProductName=NEOX
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Instalacja wymaga admina (Program Files); sama aplikacja startuje bez admina (asInvoker).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=output
OutputBaseFilename=Neox_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExe}
CloseApplications=yes

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Uruchom {#MyAppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
; Aktualizacja w tle (tryb cichy): uruchom program ponownie po instalacji, bez uprawnień administratora.
Filename: "{app}\{#MyAppExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent
