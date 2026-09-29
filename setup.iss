#define MyAppName "G-list"
#define MyAppVersion "1.4.1"
#define MyAppPublisher "G-list"
#define MyAppExeName "TodoApp.exe"

[Setup]
; Keep the existing identity so upgrading preserves the previous installation.
AppId={{5D0B2E15-D62F-4A92-A5F8-63625920D91E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppVerName={#MyAppName} {#MyAppVersion}
DefaultDirName={localappdata}\Programs\G-list
DefaultGroupName={#MyAppName}
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=.
OutputBaseFilename=todolist new
SetupIconFile=TodoApp\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=TodoApp.exe
RestartApplications=no
VersionInfoProductName={#MyAppName}
VersionInfoDescription=G-list Installer
VersionInfoVersion=1.4.1.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Dedicated self-contained publish output. User databases must never be packaged.
Source: "artifacts\gtools-win-x64\*"; DestDir: "{app}"; Excludes: "*.db,*.db-shm,*.db-wal,*.pdb,*.migrating"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; Remove only shortcuts created under the former app name during an upgrade.
Type: files; Name: "{autodesktop}\G-tools.lnk"
Type: files; Name: "{userprograms}\G-tools\G-tools.lnk"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

