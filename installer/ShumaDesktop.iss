; Inno Setup script — builds ShumaDesktop-Setup.exe
#define AppName "חוות דעת שמאי"
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{8C1F3A52-6E4B-4D0A-9B7E-2F5C1D3E9A71}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=א.א.רבייב שמאי רכב
DefaultDirName={autopf}\ShumaDesktop
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\ShumaDesktop.exe
OutputDir=..\output
OutputBaseFilename=ShumaDesktop-Setup
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
SetupIconFile=..\ShumaDesktop\app.ico
ChangesAssociations=yes

[Languages]
Name: "hebrew"; MessagesFile: "compiler:Languages\Hebrew.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\ShumaDesktop.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\ShumaDesktop.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.shuma"; ValueType: string; ValueName: ""; ValueData: "ShumaDesktop.Case"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\ShumaDesktop.Case"; ValueType: string; ValueName: ""; ValueData: "תיק שמאות"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\ShumaDesktop.Case\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\ShumaDesktop.exe,0"
Root: HKA; Subkey: "Software\Classes\ShumaDesktop.Case\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\ShumaDesktop.exe"" ""%1"""

[Run]
Filename: "{app}\ShumaDesktop.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
