<#
  Installs the kiosk on THIS PC. Run as Administrator.
  Usage:  .\Install-Kiosk.ps1 -Key "<pairing key from the admin app>"
  Optional: -KioskUser Kiosk
            -KioskPassword "..."   (a random password is created if left out)
            -BlankPassword         (no password on the Kiosk account; the PC starts fully automatically)
            -BlockCtrlAltDel       (disables the Delete keys on this PC so Ctrl+Alt+Del cannot be pressed; needs a restart)
#>
param(
    [Parameter(Mandatory = $true)][string]$Key,
    [string]$KioskUser = "Kiosk",
    [string]$KioskPassword = "",
    [switch]$BlankPassword,
    [switch]$BlockCtrlAltDel,
    [string]$InstallDir = "$env:ProgramFiles\KioskSuite"
)

# A 32-bit PowerShell on 64-bit Windows lacks the account/firewall cmdlets and redirects the registry: switch to the real 64-bit one.
if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem) {
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    foreach ($k in $PSBoundParameters.Keys) {
        $v = $PSBoundParameters[$k]
        if ($v -is [switch]) { if ($v) { $a += "-$k" } } else { $a += "-$k"; $a += [string]$v }
    }
    & "$env:SystemRoot\Sysnative\WindowsPowerShell\v1.0\powershell.exe" @a
    exit $LASTEXITCODE
}
$ErrorActionPreference = "Stop"
$log = "$env:ProgramData\KioskInstall.log"
Start-Transcript -Path $log -Force | Out-Null
trap {
    Write-Host "INSTALL FAILED: $_" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    Stop-Transcript | Out-Null
    exit 1
}

function New-RandomPassword {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 16 | ForEach-Object { [char]$_ })
}

# Native programs (reg, icacls, sc) print normal status text to the error stream. Under
# $ErrorActionPreference = Stop that would be treated as a failure, so run them through this helper
# and judge only by the exit code.
function Run-Native([string]$Exe, [string[]]$Arguments) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $Exe @Arguments 2>&1 | Out-Null; return $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Please run this script as Administrator." }

$src = $PSScriptRoot
$Key = $Key.Trim()
try { $kb = [Convert]::FromBase64String($Key) } catch { throw "The pairing key is not valid." }
if ($kb.Length -ne 32) { throw "The pairing key is not valid." }

# --- keep Microsoft Defender from quarantining the (unsigned) kiosk files ---
try { Add-MpPreference -ExclusionPath $InstallDir, "$env:ProgramData\KioskSuite" -ErrorAction Stop }
catch { Write-Warning "Could not add a Microsoft Defender exclusion: $($_.Exception.Message). If Defender quarantines kiosk files, exclude '$InstallDir' and '$env:ProgramData\KioskSuite' in Defender (or in your central policy)." }

# --- stop an older install ---
if (Get-Service KioskAgent -ErrorAction SilentlyContinue) {
    Stop-Service KioskAgent -Force -ErrorAction SilentlyContinue
    $null = Run-Native sc.exe @("delete","KioskAgent")
    Start-Sleep -Seconds 2
}

# --- program files ---
New-Item -ItemType Directory -Force "$InstallDir\Agent", "$InstallDir\Launcher" | Out-Null
Copy-Item "$src\Agent\*"    "$InstallDir\Agent\"    -Recurse -Force
Copy-Item "$src\Launcher\*" "$InstallDir\Launcher\" -Recurse -Force

# --- data folder (SIDs are used so this works on any Windows language) ---
$data = "$env:ProgramData\KioskSuite"
New-Item -ItemType Directory -Force $data | Out-Null
if ((Run-Native icacls @($data,"/inheritance:r","/grant","*S-1-5-18:(OI)(CI)F","/grant","*S-1-5-32-544:(OI)(CI)F","/grant","*S-1-5-32-545:(OI)(CI)RX")) -ne 0) { throw "icacls failed on $data" }
New-Item -ItemType Directory -Force "$data\Private" | Out-Null
if ((Run-Native icacls @("$data\Private","/inheritance:r","/grant","*S-1-5-18:(OI)(CI)F","/grant","*S-1-5-32-544:(OI)(CI)F")) -ne 0) { throw "icacls failed on $data\Private" }
Set-Content "$data\Private\agent.key" $Key -Encoding ascii -NoNewline
Set-Content "$data\kiosk.user" $KioskUser -Encoding ascii

# --- the kids' Windows account (standard user, logs in automatically) ---
# A blank password is only set at the very end; until then a random one is used so the profile can be created.
if ($BlankPassword -or $KioskPassword -eq "") { $KioskPassword = New-RandomPassword }
$securePw = ConvertTo-SecureString $KioskPassword -AsPlainText -Force
$existing = Get-LocalUser -Name $KioskUser -ErrorAction SilentlyContinue
if (-not $existing) {
    New-LocalUser -Name $KioskUser -Password $securePw -FullName "Kiosk" -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
    $usersGroup = (Get-LocalGroup -SID "S-1-5-32-545").Name
    Add-LocalGroupMember -Group $usersGroup -Member $KioskUser
} else {
    Set-LocalUser -Name $KioskUser -Password $securePw -PasswordNeverExpires $true
}

# --- auto-login ---
$wl = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"
Set-ItemProperty $wl AutoAdminLogon "1"
Set-ItemProperty $wl DefaultUserName $KioskUser
Set-ItemProperty $wl DefaultPassword $KioskPassword
Set-ItemProperty $wl DefaultDomainName $env:COMPUTERNAME
# The PC-wide shell must stay the normal Windows desktop, so every other account is untouched.
Set-ItemProperty $wl Shell "explorer.exe"

# --- give ONLY the kiosk account its own shell (our launcher) ---
$sid = (Get-LocalUser -Name $KioskUser).SID.Value
$prof = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
if (-not $prof) {
    # the profile does not exist until the account has logged in once; create it now
    $cred = New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\$KioskUser", (ConvertTo-SecureString $KioskPassword -AsPlainText -Force))
    Start-Process -FilePath "$env:SystemRoot\System32\cmd.exe" -ArgumentList "/c exit" -Credential $cred `
        -LoadUserProfile -WorkingDirectory "$env:SystemRoot\System32" -WindowStyle Hidden -Wait
    $prof = Get-CimInstance Win32_UserProfile | Where-Object { $_.SID -eq $sid }
}
if (-not $prof) { throw "Could not create the Windows profile for '$KioskUser'." }

$loadedAlready = Test-Path "Registry::HKEY_USERS\$sid"
if ($loadedAlready) { $target = "HKU\$sid" }
else {
    $target = "HKU\KioskSetupHive"
    if ((Run-Native reg @("load",$target,"$($prof.LocalPath)\NTUSER.DAT")) -ne 0) { throw "Could not load the registry of '$KioskUser'. Is the account logged in or is its profile damaged?" }
}
$exe = "$InstallDir\Launcher\KioskLauncher.exe"
$regVal = '\"' + $exe.Replace('\', '\\') + '\"'
$regFile = Join-Path $env:TEMP "kioskshell.reg"
$hk = $target.Replace('HKU','HKEY_USERS')
@("Windows Registry Editor Version 5.00", "",
  "[$hk\Software\Microsoft\Windows NT\CurrentVersion\Winlogon]",
  "`"Shell`"=`"$regVal`"", "",
  "[$hk\Software\Microsoft\Windows\CurrentVersion\Policies\System]",
  "`"DisableTaskMgr`"=dword:00000001",
  "`"DisableLockWorkstation`"=dword:00000001",
  "`"DisableChangePassword`"=dword:00000001", "",
  "[$hk\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer]",
  "`"NoLogoff`"=dword:00000001") | Set-Content $regFile -Encoding Unicode
$rc = Run-Native reg @("import",$regFile)
Remove-Item $regFile -ErrorAction SilentlyContinue
if (-not $loadedAlready) { [gc]::Collect(); Start-Sleep -Seconds 1; $null = Run-Native reg @("unload",$target) }
if ($rc -ne 0) { throw "Could not set the shell for '$KioskUser'." }

# --- hide "Switch user" on the Ctrl+Alt+Del screen (machine-wide) ---
$sysPol = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
if (-not (Test-Path $sysPol)) { New-Item -Path $sysPol | Out-Null }
Set-ItemProperty $sysPol HideFastUserSwitching 1 -Type DWord

# --- Windows 10/11 can refuse automatic login ("Require Windows Hello sign-in"); switch that off ---
$pl = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device"
if (Test-Path $pl) { Set-ItemProperty $pl DevicePasswordLessBuildVersion 0 -Type DWord }

# --- optional: no password on the Kiosk account ---
if ($BlankPassword) {
    try {
        Set-LocalUser -Name $KioskUser -Password (New-Object System.Security.SecureString)
        Set-ItemProperty $wl DefaultPassword ""
        $KioskPassword = ""
    } catch {
        Write-Warning "Windows would not allow a blank password (a password policy is set). The account keeps a random password and still logs in automatically."
    }
}

# --- optional: make Ctrl+Alt+Del impossible by disabling both Delete keys (restart needed) ---
if ($BlockCtrlAltDel) {
    $kl = "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layout"
    $map = [byte[]](0,0,0,0, 0,0,0,0, 3,0,0,0, 0,0,0x53,0xE0, 0,0,0x53,0x00, 0,0,0,0)
    Set-ItemProperty $kl -Name "Scancode Map" -Value $map -Type Binary
}

# --- agent service ---
New-Service -Name KioskAgent -BinaryPathName "`"$InstallDir\Agent\KioskAgent.exe`"" -DisplayName "Kiosk Agent" `
    -Description "Receives kiosk setup from the Kiosk Admin app" -StartupType Automatic | Out-Null
$null = Run-Native sc.exe @("failure","KioskAgent","reset=","86400","actions=","restart/5000/restart/5000/restart/5000")
$null = Run-Native sc.exe @("failureflag","KioskAgent","1")
Get-NetFirewallRule -DisplayName "Kiosk Agent" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName "Kiosk Agent" -Direction Inbound -Protocol TCP -LocalPort 48650 -Action Allow -Profile Any | Out-Null
& ([scriptblock]::Create((Get-Content "$PSScriptRoot\Setup-Network.ps1" -Raw)))   # Private network + discovery + sharing
Start-Service KioskAgent
Stop-Transcript | Out-Null

Write-Host ""
Write-Host "Kiosk installed." -ForegroundColor Green
Write-Host "Windows account for the kids: $KioskUser"
if ($KioskPassword -ne "") { Write-Host "Its password (used for auto-login): $KioskPassword" } else { Write-Host "The account has no password and logs in automatically." }
if ($BlockCtrlAltDel) { Write-Host "Ctrl+Alt+Del and the Delete keys are blocked after the next restart." }
Write-Host ""
Write-Host "Last steps on this PC:"
Write-Host " 1. Log in once as '$KioskUser', open Steam, sign in with 'Remember password', and turn OFF"
Write-Host "    Steam > Settings > In Game > 'Enable the Steam Overlay while in-game'."
Write-Host " 2. Restart the PC. It will log in as '$KioskUser' and show the launcher."
Write-Host " 3. Add this PC in the Kiosk Admin app and push your setup."
Write-Host " Leave kiosk mode any time with Ctrl+Alt+Shift+K (password: 'change-me' until you push your own)."
