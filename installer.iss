#ifndef MyAppVersion
#define MyAppVersion "1.0.21"
#endif

[Setup]
AppName=LayoutFix
AppId=Wave-is.LayoutFix
AppVersion={#MyAppVersion}
AppPublisher=Wave-is
AppPublisherURL=https://github.com/Wave-is/LayoutFix
DefaultDirName={localappdata}\Programs\LayoutFix
DefaultGroupName=LayoutFix
OutputDir=Output
#ifdef LayoutFixTestInstall
OutputBaseFilename=LayoutFix_Setup_Test
Compression=none
SolidCompression=no
Uninstallable=yes
CreateUninstallRegKey=no
#else
OutputBaseFilename=LayoutFix_Setup
Compression=lzma
SolidCompression=yes
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription=LayoutFix Setup
UninstallDisplayIcon={app}\LayoutFix.exe
AppMutex=LayoutFix_SingleInstance_Mutex
CloseApplications=yes
RestartApplications=no
[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "autostart"; Description: "Run LayoutFix automatically when Windows starts"; GroupDescription: "System integration:"; Flags: unchecked

[Files]
Source: "src\LayoutFix\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LayoutFix"; Filename: "{app}\LayoutFix.exe"
Name: "{autodesktop}\LayoutFix"; Filename: "{app}\LayoutFix.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LayoutFix"; ValueData: """{app}\LayoutFix.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\LayoutFix.exe"; Description: "Launch LayoutFix now"; Flags: nowait postinstall skipifsilent
