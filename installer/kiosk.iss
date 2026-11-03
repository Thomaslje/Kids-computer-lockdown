; Inno Setup script - builds dist\KioskSetup.exe (the installer for the kids' PCs)
[Setup]
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppName=Kiosk Launcher
AppVersion=1.0
DefaultDirName={autopf}\KioskSuite\Setup
PrivilegesRequired=admin
OutputDir=..\dist
OutputBaseFilename=KioskSetup
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
Uninstallable=no
WizardStyle=modern

[Files]
Source: "..\dist\kiosk\*"; DestDir: "{tmp}\ks"; Flags: recursesubdirs createallsubdirs ignoreversion deleteafterinstall

[Code]
var
  KeyPage: TInputQueryWizardPage;
  OptPage: TInputOptionWizardPage;

procedure InitializeWizard;
begin
  KeyPage := CreateInputQueryPage(wpWelcome, 'Pairing key',
    'Paste the pairing key from the Kiosk Admin app (button "Pairing key...")', '');
  KeyPage.Add('Pairing key:', False);

  OptPage := CreateInputOptionPage(KeyPage.ID, 'Options', 'How should this kiosk PC behave?',
    'Choose what you want:', False, False);
  OptPage.Add('No password on the Windows account "Kiosk" (the PC starts fully automatically)');
  OptPage.Add('Block Ctrl+Alt+Del (turns off the Delete keys on this PC; needs a restart)');
  OptPage.Values[0] := True;
  OptPage.Values[1] := True;
end;

function GetKey(Param: String): String;
begin
  Result := Trim(KeyPage.Values[0]);
end;

function GetOpts(Param: String): String;
begin
  Result := '';
  if OptPage.Values[0] then Result := Result + ' -BlankPassword';
  if OptPage.Values[1] then Result := Result + ' -BlockCtrlAltDel';
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = KeyPage.ID) and (Length(Trim(KeyPage.Values[0])) < 40) then
  begin
    MsgBox('Please paste the full pairing key.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Installing kiosk...';
    if (not Exec('powershell.exe',
         '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\ks\Install-Kiosk.ps1') +
         '" -Key "' + GetKey('') + '"' + GetOpts(''), '', SW_HIDE, ewWaitUntilTerminated, Code)) or (Code <> 0) then
      MsgBox('The kiosk install FAILED. Details: C:\ProgramData\KioskInstall.log', mbError, MB_OK)
    else
      MsgBox('Kiosk installed. Log in once as "Kiosk", set up Steam, then restart the PC.', mbInformation, MB_OK);
  end;
end;
