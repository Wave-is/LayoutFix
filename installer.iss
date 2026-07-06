[Setup]
AppName=LayoutFix
AppVersion=1.0.9
AppPublisher=Wave-is
AppPublisherURL=https://github.com/Wave-is/LayoutFix
DefaultDirName={autopf}\LayoutFix
DefaultGroupName=LayoutFix
OutputBaseFilename=LayoutFix_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\LayoutFix.exe
AppMutex=LayoutFix_SingleInstance_Mutex
CloseApplications=force
[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked
Name: "autostart"; Description: "Run LayoutFix automatically when Windows starts"; GroupDescription: "System integration:"

[Files]
Source: "src\LayoutFix\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Dictionaries\*"; DestDir: "{app}\Dictionaries"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Sounds\*"; DestDir: "{app}\Sounds"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "locales\*"; DestDir: "{app}\locales"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LayoutFix"; Filename: "{app}\LayoutFix.exe"
Name: "{autodesktop}\LayoutFix"; Filename: "{app}\LayoutFix.exe"; Tasks: desktopicon
Name: "{userstartup}\LayoutFix"; Filename: "{app}\LayoutFix.exe"; Tasks: autostart

[Run]
Filename: "{app}\LayoutFix.exe"; Description: "Launch LayoutFix now"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill.exe', '/F /IM LayoutFix.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('taskkill.exe', '/F /IM LayoutFix.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
