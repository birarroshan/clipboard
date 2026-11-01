#define MyAppName "ClipBoard"
#define MyAppPublisher "birarroshan"
#define MyAppURL "https://github.com/birarroshan/clipboard"
#define MyAppVersion AppVersion

[Setup]
AppId={{5D1B0EEB-83E1-4C17-9FDE-4FEA27E6E88E}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={commonpf64}\ClipBoard
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
InfoBeforeFile=Welcome.txt
PrivilegesRequired=admin
OutputBaseFilename=ClipBoard-{#MyAppVersion}-win-x64-setup
OutputDir={#OutputDir}
Compression=lzma2
SolidCompression=yes
CreateAppDir=yes
UninstallDisplayIcon={app}\ClipBoard.exe
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "{#AppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Welcome.txt"; DestDir: "{tmp}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClipBoard"; Filename: "{app}\ClipBoard.exe"
Name: "{commondesktop}\ClipBoard"; Filename: "{app}\ClipBoard.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:";
