#ifndef MyAppVersion
#define MyAppVersion "1.0.26"
#endif

#ifndef MyAppPayloadDirectory
#define MyAppPayloadDirectory "src\LayoutFix\bin\Release\net8.0-windows\win-x64\publish"
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
Source: "{#MyAppPayloadDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifndef LayoutFixTestInstall
Name: "{group}\LayoutFix"; Filename: "{app}\LayoutFix.exe"
Name: "{autodesktop}\LayoutFix"; Filename: "{app}\LayoutFix.exe"; Tasks: desktopicon
#endif

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LayoutFix"; ValueData: """{app}\LayoutFix.exe"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\LayoutFix.exe"; Description: "Launch LayoutFix now"; Flags: nowait postinstall skipifsilent
#ifdef LayoutFixTestInstall
Filename: "{app}\LayoutFix.exe"; Parameters: "--smoke-test"; Flags: waituntilterminated runasoriginaluser; Check: ShouldLaunchAfterSilentUpdate
#else
Filename: "{app}\LayoutFix.exe"; Flags: nowait runasoriginaluser; Check: ShouldLaunchAfterSilentUpdate
#endif

[Code]
function HasExactCommandLineSwitch(const Value: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Value) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function ShouldLaunchAfterSilentUpdate(): Boolean;
begin
  Result := WizardSilent and HasExactCommandLineSwitch('/LAUNCHAFTERUPDATE');
end;

function InitializeSetup(): Boolean;
var
  Attempt: Integer;
begin
  Result := True;
  if not HasExactCommandLineSwitch('/LAUNCHAFTERUPDATE') then Exit;
  for Attempt := 1 to 300 do begin
    if not CheckForMutexes('LayoutFix_SingleInstance_Mutex') then Exit;
    Sleep(100);
  end;
  Log('The running LayoutFix instance did not finish shutting down.');
  Result := False;
end;
