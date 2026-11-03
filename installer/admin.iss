; Inno Setup script - builds dist\KioskAdminSetup.exe (the installer for your admin PC)
[Setup]
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppName=Kiosk Admin
AppVersion=1.0
DefaultDirName={autopf}\KioskAdmin
DefaultGroupName=Kiosk Admin
OutputDir=..\dist
OutputBaseFilename=KioskAdminSetup
WizardStyle=modern

[Files]
Source: "..\scripts\Setup-Network.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\admin\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Kiosk Admin"; Filename: "{app}\KioskAdmin.exe"
Name: "{autodesktop}\Kiosk Admin"; Filename: "{app}\KioskAdmin.exe"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Setup-Network.ps1"""; Flags: runhidden waituntilterminated; StatusMsg: "Setting the network to Private and turning on discovery..."
Filename: "{app}\KioskAdmin.exe"; Description: "Start Kiosk Admin"; Flags: nowait postinstall skipifsilent
