; nugsdotnet installer — per-user, no UAC. Version/publish dir are passed by the
; build (/DMyAppVersion=, /DPublishDir=). Defaults below are for local manual runs.
#ifndef MyAppVersion
  #define MyAppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\Nugsdotnet.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
#endif

#define MyAppName "nugsdotnet"
#define MyAppPublisher "Tim Vanbenschoten"
#define MyAppURL "https://github.com/tsvb/nugsdotnet"
#define MyAppExeName "Nugsdotnet.App.exe"

[Setup]
; AppId is the winget/identity anchor — never change it.
AppId={{8B3F2A14-9C7D-4E6B-A1F0-5D2E7C9B4A60}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=nugsdotnet-{#MyAppVersion}-x64-setup
SetupIconFile=nugsdotnet.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
