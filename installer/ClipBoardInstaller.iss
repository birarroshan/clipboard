#define MyAppName "ClipStack"
#define MyAppPublisher "birarroshan"
#define MyAppURL "https://github.com/birarroshan/clipboard"
#define MyAppVersion AppVersion
#ifndef AppArchitecture
	#define AppArchitecture "x64"
#endif
#if (AppArchitecture != "x64") && (AppArchitecture != "x86")
	#error Unsupported AppArchitecture; use x64 or x86.
#endif

[Setup]
AppId={{5D1B0EEB-83E1-4C17-9FDE-4FEA27E6E88E}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\ClipStack
DefaultGroupName={#MyAppName}
UsePreviousGroup=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
InfoBeforeFile=Welcome.txt
PrivilegesRequired=admin
SetupIconFile=..\assets\branding\ClipStack.ico
WizardImageFile=..\assets\branding\installer-wizard.bmp
WizardSmallImageFile=..\assets\branding\installer-small.bmp
OutputBaseFilename=ClipStack-{#MyAppVersion}-win-{#AppArchitecture}-setup
OutputDir={#OutputDir}
Compression=lzma2
SolidCompression=yes
CreateAppDir=yes
UninstallDisplayIcon={app}\ClipStack.exe
#if AppArchitecture == "x64"
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#else
ArchitecturesAllowed=x86compatible
#endif

[Files]
Source: "{#AppSourceDir}\ClipStack.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "Welcome.txt"; DestDir: "{tmp}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClipStack"; Filename: "{app}\ClipStack.exe"
Name: "{commondesktop}\ClipStack"; Filename: "{app}\ClipStack.exe"; Tasks: desktopicon

[InstallDelete]
Type: files; Name: "{app}\ClipBoard.exe"
Type: files; Name: "{autoprograms}\ClipBoard\ClipBoard.lnk"
Type: files; Name: "{commondesktop}\ClipBoard.lnk"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:";
