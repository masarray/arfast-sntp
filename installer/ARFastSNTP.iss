#define Payload "..\publish"
[Setup]
AppId={{A9AEFEC9-42AE-4BDC-A69C-F2053D0D97A0}
AppName=AR Fast SNTP
AppVersion=0.9.0
DefaultDirName={autopf}\ARFastSNTP
UsePreviousPrivileges=yes
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
OutputDir=..\installer-out
OutputBaseFilename=ARFastSNTP-Setup-Windows-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
UninstallDisplayIcon={app}\studio\ARFastSNTP.Studio.exe

[Files]
Source: "{#Payload}\worker\*"; DestDir: "{app}\worker"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\studio\*"; DestDir: "{app}\studio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\dashboard\*"; DestDir: "{app}\dashboard"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\ARFastSNTP.Setup.exe"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "{#Payload}\ARFastSNTP.Setup.exe"; Flags: dontcopy
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\AR Fast SNTP Studio"; Filename: "{app}\studio\ARFastSNTP.Studio.exe"

[Run]
Filename: "{app}\studio\ARFastSNTP.Studio.exe"; Description: "Open SNTP Live Monitor"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var
  ServerPage: TInputQueryWizardPage;
  ServerIp: String;
  ServerYear: String;

function ExtractArgument(const Source, Token: String): String;
var P, E: Integer;
begin
  Result := '';
  P := Pos(Token + ' ', Source);
  if P = 0 then Exit;
  P := P + Length(Token) + 1;
  E := P;
  while (E <= Length(Source)) and (Source[E] <> ' ') do E := E + 1;
  Result := Copy(Source, P, E - P);
end;

procedure InitializeWizard;
var Existing, Ip, Yr: String;
begin
  Existing := '';
  RegQueryStringValue(HKLM, 'SYSTEM\CurrentControlSet\Services\ARFastSNTP', 'ImagePath', Existing);
  Ip := ExtractArgument(Existing, '--server');
  Yr := ExtractArgument(Existing, '--server-year');
  if Ip = '' then Ip := '192.16.1.175';
  if Yr = '' then Yr := '2026';
  if ExpandConstant('{param:SERVER|}') <> '' then Ip := ExpandConstant('{param:SERVER|}');
  if ExpandConstant('{param:YEAR|}') <> '' then Yr := ExpandConstant('{param:YEAR|}');
  ServerPage := CreateInputQueryPage(wpWelcome, 'NTP server',
    'Install and synchronize', 'Enter the trusted server for this isolated certification PC.');
  ServerPage.Add('Server IPv4:', False);
  ServerPage.Add('Reference year:', False);
  ServerPage.Values[0] := Ip;
  ServerPage.Values[1] := Yr;
end;

function NextButtonClick(PageID: Integer): Boolean;
var Y: Integer;
begin
  Result := True;
  if PageID <> ServerPage.ID then Exit;
  ServerIp := Trim(ServerPage.Values[0]);
  ServerYear := Trim(ServerPage.Values[1]);
  Y := StrToIntDef(ServerYear, 0);
  if (Y < 2020) or (Y > 2090) or (ServerIp = '') then
  begin
    MsgBox('Enter a valid server IP and reference year (2020-2090).', mbError, MB_OK);
    Result := False;
  end;
end;

function RunHelper(const Executable, Params: String): Boolean;
var ExitCode: Integer;
begin
  Result := Exec(Executable, Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  if Result then Result := ExitCode = 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ExtractTemporaryFile('ARFastSNTP.Setup.exe');
  if not RunHelper(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe'),
    '--inno-prepare --server ' + ServerIp + ' --year ' + ServerYear) then
    Result := 'Cannot prepare the existing service. See ProgramData\ARFastSNTP\setup.log.';
end;

procedure CurStepChanged(Step: TSetupStep);
var Flags: String;
begin
  if Step <> ssPostInstall then Exit;
  Flags := '--inno-activate --server ' + ServerIp + ' --year ' + ServerYear;
  if ExpandConstant('{param:LABSYNC|1}') <> '0' then
    Flags := Flags + ' --lab'
  else
    Flags := Flags + ' --monitor-only';
  if not RunHelper(ExpandConstant('{app}\setup\ARFastSNTP.Setup.exe'), Flags) then
    RaiseException('Service could not start. Check ProgramData\ARFastSNTP\setup.log.');
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if Step = usUninstall then
    if not RunHelper(ExpandConstant('{app}\setup\ARFastSNTP.Setup.exe'), '--inno-remove') then
      RaiseException('Service could not be removed; uninstall cancelled.');
end;

procedure DeinitializeSetup;
begin
  { If setup was cancelled or failed after preflight, restore the old service.
    On successful install the recovery snapshot is absent, so this is a no-op. }
  if FileExists(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe')) then
  begin
    if not RunHelper(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe'),
      '--inno-abort') then
      Log('Recovery after cancelled setup needs administrator attention: ' +
        'C:\ProgramData\ARFastSNTP\setup.log');
  end;
end;
