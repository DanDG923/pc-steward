; Private-beta installer configuration. Compile only with a product-owner supplied,
; signed release payload and Inno Setup 6 (ISCC.exe); do not distribute unsigned output.
#define ProductName "PC Steward"
#define ProductVersion "0.1.0-private-beta"
#define PayloadDir "..\\artifacts\\private-beta\\payload"

[Setup]
AppId={{BCA53DB5-2F21-4416-A76A-98FE2E07D9ED}
AppName={#ProductName}
AppVersion={#ProductVersion}
AppPublisher=PC Steward
DefaultDirName={autopf}\PC Steward
DefaultGroupName=PC Steward
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayName=PC Steward
OutputDir=..\artifacts\private-beta\installer
OutputBaseFilename=PCSteward-0.1.0-private-beta-setup
SetupIconFile=..\design\assets\ico\pc-steward.ico
Compression=lzma2
SolidCompression=yes

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\PC Steward"; Filename: "{app}\PCSteward.Desktop.exe"

[Run]
Filename: "{app}\PCSteward.Desktop.exe"; Description: "Launch PC Steward"; Flags: nowait postinstall skipifsilent

[Code]
function ShouldRemoveUserData(): Boolean;
begin
  Result := MsgBox('Remove this Windows user''s PC Steward data as well? Choosing No retains it for a later reinstall.',
    mbConfirmation, MB_YESNO) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot: String;
  ResultCode: Integer;
begin
  if CurUninstallStep = usPostUninstall then begin
    Exec('schtasks.exe', '/Delete /TN "PCSteward.ScheduledCheck" /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if ShouldRemoveUserData() then begin
      DataRoot := ExpandConstant('{localappdata}\PCSteward');
      DelTree(DataRoot, True, True, True);
    end;
  end;
end;
