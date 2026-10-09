#define Payload "..\publish"
[Setup]
AppId={{A9AEFEC9-42AE-4BDC-A69C-F2053D0D97A0}
AppName=AR Fast SNTP
AppVersion=0.10.1
DefaultDirName={autopf}\ARFastSNTP
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
WizardSizePercent=100,100
WizardResizable=no
OutputDir=..\installer-out
OutputBaseFilename=ARFastSNTP-Setup-Windows-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
SetupMutex=ARFastSNTP-Upgrade-Guard
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\studio\ARFastSNTP.Studio.exe

[Files]
Source: "{#Payload}\worker\*"; DestDir: "{app}\worker"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\studio\*"; DestDir: "{app}\studio"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Payload}\ARFastSNTP.Setup.exe"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "{#Payload}\ARFastSNTP.Setup.exe"; Flags: dontcopy
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\NOTICE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\DESIGN-TOKEN-PROVENANCE.md"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Registry]
; Service-owned ON/OFF preference persists across upgrades and reboots,
; but is removed on an explicit full uninstall.
Root: HKLM; Subkey: "SOFTWARE\ARFastSNTP"; Flags: uninsdeletekey

[UninstallDelete]
; A full uninstall removes NTP source preferences, but preserves diagnostics.
Type: files; Name: "{commonappdata}\ARFastSNTP\peers.json"

[Icons]
Name: "{autoprograms}\AR Fast SNTP"; Filename: "{app}\studio\ARFastSNTP.Studio.exe"

[Run]
Filename: "{app}\studio\ARFastSNTP.Studio.exe"; Description: "Open NTP Monitor"; Flags: postinstall nowait skipifsilent runasoriginaluser

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

function ValidIp(const Ip: String): Boolean;
var I, Dots, Digits, Octet: Integer; C: Char;
begin
  Result := False; Dots := 0; Digits := 0; Octet := 0;
  for I := 1 to Length(Ip) do
  begin
    C := Ip[I];
    if C = '.' then
    begin
      if (Digits = 0) or (Octet > 255) then Exit;
      Dots := Dots + 1; Digits := 0; Octet := 0;
    end
    else if (C >= '0') and (C <= '9') then
    begin
      Digits := Digits + 1;
      Octet := Octet * 10 + Ord(C) - Ord('0');
      if (Digits > 3) or (Octet > 255) then Exit;
    end
    else Exit;
  end;
  Result := (Dots = 3) and (Digits > 0);
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
  if (StrToIntDef(Yr, 0) < 2020) or (StrToIntDef(Yr, 0) > 2090) then Yr := '2026';
  ServerPage := CreateInputQueryPage(wpWelcome, 'NTP Server',
    'Enter NTP Server IP Address',
    'Enter your first NTP server. More can be added later in AR Fast SNTP.');
  ServerPage.Add('Server IP Address:', False);
  ServerPage.Values[0] := Ip;
  ServerIp := Ip;
  ServerYear := Yr;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ServerPage.ID) and
    FileExists(ExpandConstant('{commonappdata}\ARFastSNTP\peers.json'));
  { An existing multi-server configuration belongs to the installed service.
    Never overwrite it merely because Setup is being upgraded. }
end;

function NextButtonClick(PageID: Integer): Boolean;
begin
  Result := True;
  if PageID <> ServerPage.ID then Exit;
  ServerIp := Trim(ServerPage.Values[0]);
  if not ValidIp(ServerIp) then
  begin
    MsgBox('Enter a valid NTP server IP address.', mbError, MB_OK);
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
  WizardForm.StatusLabel.Caption := 'Checking existing NTP service...';
  if not RunHelper(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe'),
    '--inno-prepare --server ' + ServerIp + ' --year ' + ServerYear) then
    Result := 'Cannot prepare NTP service. See C:\ProgramData\ARFastSNTP\setup.log.';
end;

procedure CurStepChanged(Step: TSetupStep);
var Flags: String;
begin
  if Step <> ssPostInstall then Exit;
  WizardForm.StatusLabel.Caption := 'Starting NTP service and checking the server...';
  Flags := '--inno-activate --server ' + ServerIp + ' --year ' + ServerYear;
  if ExpandConstant('{param:LABSYNC|1}') <> '0' then Flags := Flags + ' --lab'
  else Flags := Flags + ' --monitor-only';
  if not RunHelper(ExpandConstant('{app}\setup\ARFastSNTP.Setup.exe'), Flags) then
    RaiseException('NTP service did not start. Check C:\ProgramData\ARFastSNTP\setup.log.');
end;

procedure CurPageChanged(PageID: Integer);
var
  Health: String;
  RawHealth: AnsiString;
begin
  if PageID <> wpFinished then Exit;
  Health := 'WAITING';
  if LoadStringFromFile(
    ExpandConstant('{commonappdata}\ARFastSNTP\install-result.txt'), RawHealth) then
    Health := Trim(String(RawHealth));
  if ExpandConstant('{param:LABSYNC|1}') = '0' then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'NTP monitoring is ready';
    WizardForm.FinishedLabel.Caption := 'NTP service is running in monitor mode.' + #13#10 +
      'PC time will not be changed.';
  end
  else if Health = 'SYNCED' then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'PC time is synchronized';
    WizardForm.FinishedLabel.Caption := 'NTP service is running.' + #13#10 +
      'Your PC time was verified with the NTP server.' + #13#10 +
      'Sync keeps running after you close the monitor.';
  end
  else if Health = 'REPLY' then
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'NTP service is running';
    WizardForm.FinishedLabel.Caption := 'NTP server responded. PC time is still being checked.' + #13#10 +
      'Open the monitor to see progress.';
  end
  else
  begin
    WizardForm.FinishedHeadingLabel.Caption := 'NTP service is running';
    WizardForm.FinishedLabel.Caption := 'No NTP reply has been verified yet.' + #13#10 +
      'Open the monitor to check the connection.';
  end;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if Step = usUninstall then
    if not RunHelper(ExpandConstant('{app}\setup\ARFastSNTP.Setup.exe'), '--inno-remove') then
      RaiseException('Cannot stop NTP service. Uninstall cancelled.');
end;

procedure DeinitializeSetup;
begin
  if FileExists(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe')) then
    if not RunHelper(ExpandConstant('{tmp}\ARFastSNTP.Setup.exe'), '--inno-abort') then
      Log('NTP recovery needs attention: C:\ProgramData\ARFastSNTP\setup.log');
end;
