# Kiosk Suite

Three parts, one pairing key:

| Part | Where | What it does |
|---|---|---|
| **KioskAdmin** | your PC | Build the tile list (Steam games, programs, Chrome), set the exit password, push to PCs |
| **KioskAgent** (service) | each kids' PC | Receives the setup from the admin app (encrypted, key-authenticated, TCP 48650) and locks Chrome down with policies |
| **KioskLauncher** | each kids' PC | Fullscreen tile screen, runs as the Windows shell for the `Kiosk` account, blocks Win/Alt+Tab/Task Manager, restarts itself if it crashes |

## 1. Build (once, on any Windows PC)
1. Install the .NET 8 SDK (free). Optional: Inno Setup 6 for single-file installers.
2. In PowerShell: `.\Build.ps1`
3. Result in `dist\`:
   - With Inno Setup: `KioskAdminSetup.exe` (your PC) and `KioskSetup.exe` (kids' PCs)
   - Without: `dist\admin\KioskAdmin.exe` and the folder `dist\kiosk`

## 2. Your PC
Start **Kiosk Admin**. Click **Pairing key...** and copy the key (it is created on first start; keep it private).

## 3. Each kids' PC
- With the installer: run `KioskSetup.exe`, paste the key.
- Without: copy `dist\kiosk` over, then as Administrator: `Install.cmd -Key "<key>"`.

Then on that PC:
1. Log in once as **Kiosk**, open Steam, sign in (Remember password), and turn off *Settings > In Game > Enable the Steam Overlay* (the overlay has a web browser and friends list that would be a way out).
2. Restart. The PC logs in as Kiosk and shows the launcher.

## 4. Set up the tiles
In Kiosk Admin: **Add PC...** (name or IP) > **+ Steam game...** (loads the games straight from a kids' PC) / **+ Program...** / **+ Chrome** > **Exit password...** > **Push to ALL PCs**.
New PCs: install, add to the list, push. The same setup goes everywhere.

## Kiosk account options (installer page, or script switches)
- **No password** (`-BlankPassword`): the PC logs in as Kiosk by itself with no password stored anywhere. Windows still blocks passwordless accounts from logging in over the network. If a password policy forbids it, the account keeps a random password and still auto-logs in.
- **Block Ctrl+Alt+Del** (`-BlockCtrlAltDel`): disables both Delete keys machine-wide (needs a restart), because Windows handles Ctrl+Alt+Del below every program and it can't be intercepted any other way. Side effect: nobody can use the Delete key on that PC. Undo with `Uninstall-Kiosk.ps1`, or delete the registry value `Scancode Map` under `HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout` and restart.
Without that option, the Ctrl+Alt+Del screen is emptied for the Kiosk account (no Task Manager, Lock, Sign out, Change password, Switch user); only the power button remains.

## Kiosk options from the admin app
Select PCs > **Kiosk options...** sets two things remotely: a new password for the Kiosk Windows account (for example `6700`; automatic login is updated to match, and the password is never stored in the admin app) and the Ctrl+Alt+Del block and offers to restart the PCs. The **Software** column shows which build each PC runs ("older" = before this column existed). The window title shows the admin app's own build.

## Remote control (Kiosk Admin, left side)
Select one or more PCs, then:
- **Unlock to desktop**: same as Ctrl+Alt+Shift+K, remotely, no password needed (the pairing key is the authority). Restart the PC to lock it again.
- **Restart** / **Shut down**: 10 second countdown shown on the kids' PC.
These need the new agent on each PC, so re-run the installer once after updating.

## Updating the software on the kids' PCs (no visiting each PC)
1. Unzip the new revision and run `.\Build.ps1 -Quick` (builds only the kids' PC software, no installers).
2. In Kiosk Admin: select the PCs, click **Update software...**, pick the `dist\kiosk` folder.
Only the files that changed are sent. The agent swaps them in (old files are renamed, not overwritten), the tile screen restarts by itself, and the agent restarts itself if it changed.
Every new revision comes as a new `KioskSuite.zip`: unzip, `.\Build.ps1`, then **Update software...**. The install scripts only matter for a brand-new PC. The Kiosk Admin app itself is updated on your PC with `KioskAdminSetup.exe` / the `dist\admin` folder.

## Going back to the kiosk after leaving it
After **Unlock to desktop** (or Ctrl+Alt+Shift+K) the Kiosk account shows the normal desktop with a **Return to Kiosk** icon. Click it and the kiosk screen comes back without restarting the PC. The agent creates the icon by itself (it needs the Kiosk account to have logged in once).

## Microsoft Defender
The programs are not code-signed and use things antivirus heuristics dislike (keyboard hook, replacing the shell, a service that accepts commands), so Defender may quarantine files. The installer and the agent add a Defender exclusion for `C:\Program Files\KioskSuite` and `C:\ProgramData\KioskSuite`. On the PC where you build, exclude the build folder too: `Add-MpPreference -ExclusionPath "C:\temp\KioskSuite"`. If Defender is managed centrally (Intune/GPO), add those paths there.

## Uninstalling remotely
Select PCs > **Uninstall kiosk...** removes the agent service, the launcher, the auto-login, the Chrome policies this program added, the hidden "Switch user", the Delete-key block (after a restart), the firewall rule and the Defender exclusions, and puts Explorer back as the shell. The optional box also deletes the Kiosk Windows account and its files, but only if it is a standard user whose full name is "Kiosk" (so an account that existed before is never deleted). With the box off, the account stays but its kiosk settings are cleaned out. The PC logs the Kiosk account off, restarts to the normal login screen, and is removed from the admin list. Log: `C:\Windows\Temp\KioskUninstall.log` on the PC. Not restored: the Windows setting for password-less sign-in that the options may have changed.

## Leaving kiosk mode
`Ctrl + Alt + Shift + K` on the kiosk PC, then the exit password. (Until you push your own: `change-me`.)
Test the screen on any PC without locking it: `KioskLauncher.exe --ui --windowed`.

## If something goes wrong
- Only the `Kiosk` account uses the launcher as its shell; every other account keeps the normal desktop.
- Kiosk account shows a black screen: sign in as admin, run `Uninstall-Kiosk.ps1 -RemoveKioskUser`, or re-run the installer.
- Uninstall: `Uninstall-Kiosk.ps1` as Administrator restores the normal desktop.
- Re-installing over a running kiosk: log the Kiosk account off first.

## Notes
- Standalone game paths and the Steam folder must be the same on every PC (as in your setup). Tiles for games that aren't installed on a PC are hidden there.
- Auto-login stores the Kiosk account password in the registry (standard Windows auto-login). The account is a standard, non-admin user.
- Windows still lets people power off from Ctrl+Alt+Del.

## Network
Both installers set the network profile to Private and turn on network discovery and file/printer sharing (`scripts/Setup-Network.ps1`). The kiosk agent repeats this every 10 minutes, so a new Wi-Fi network that Windows marks as Public is fixed automatically. Networks joined to a domain are left alone.

## Tweaks
**Tweaks...** in the admin app sends power tweaks to the selected PCs (High performance plan, screen and sleep never, Fast Startup off, network card and USB power saving off, optional Wake-on-LAN, wake timers, CPU minimum). Your ticks are remembered. Add more tweaks in `Shared/Tweaks.cs` and `Agent/TweakApplier.cs`.

## Tools menu
**Tools** in the admin app: create a password-free `C:\Share` (shared as "Share") on the PCs; send files into it (this goes through the agent, so it works even if Windows sharing does not); install a program silently (an .exe/.msi from the admin PC, or one already on the PCs, plus the silent switch such as `/S`; NVIDIA drivers use `-s -noreboot`); and start a program on the kids' screen without it being a tile (saved list, the NVIDIA Control Panel is preset). Installers run as SYSTEM in the background, so they cannot show windows or ask questions.

## Admin tools menu
**Admin tools** in the admin app. "Open on the PC's screen" starts CMD, PowerShell, Task Manager, Registry Editor, Device Manager, Services, Event Viewer, Computer Management, Task Scheduler, Programs and Features, Network connections, Resource Monitor, System Information or any program on the screen of the person logged in, running as SYSTEM (admin rights; the Kiosk account's Task Manager block does not apply to SYSTEM). "Console, processes, ..." opens a window for one PC with: a remote console (PowerShell or CMD, output shown live, with quick commands including Windows Update), process list with Kill, services with Start/Stop/Restart, installed programs, and a screenshot of the kids' screen. Everything runs as SYSTEM.

## Start password
**Start password...** (next to "Pairing key...") makes the admin app ask for a password when it opens (5 tries, with growing waits). Leave both fields empty to remove it. It is stored as a salted hash in `%APPDATA%\KioskAdmin\admin.json`. If you forget it, close the admin app and delete the `LockHash` line in that file. Note the lock only stops people using the app: the pairing key in the same file is not encrypted, so keep the PC itself protected.

## Friendly password
With a Start password set, **Friendly password...** adds a second password. When the admin app opens, the admin password gives the full app, and the friendly password gives a simple window with only the PC list, Restart and Shut down (with a confirmation). Removing the Start password also removes the friendly one. If you forget the admin password, delete the `LockHash` and `UserLockHash` lines in `%APPDATA%\KioskAdmin\admin.json`.

## TeamViewer ID
The PC list (admin and friendly view) shows each PC's TeamViewer ID, read by the agent from the registry (needs software revision 2026-10-28-a on the PC). Right-click a PC > "Copy TeamViewer ID".

## Close first (Roblox)
Select a program tile > **Close first...** and list program names (no .exe, comma separated). When the tile is clicked, any of those programs still running are closed (and checked to be gone), then the program starts; more clicks within 20 seconds just say "starting, please wait". Program tiles whose path contains "roblox" get RobloxPlayerBeta, RobloxPlayerLauncher and RobloxCrashHandler automatically. Needs the new launcher (Update software...) and a push.

## Update-proof program paths
A program tile's path may use `*` (in any folder or file name) and environment variables, for example `%LOCALAPPDATA%\Roblox\Versions\*\RobloxPlayerLauncher.exe`. The launcher expands it on the kids' PC (inside the Kiosk account) and starts the newest match. When you add a program or use **Change path...** and paste a path with a user folder or a version folder, the admin app offers to convert it automatically (`version-<hex>`, `app-1.2.3`, `1.2.3.4` folders and `C:\Users\<name>\AppData\Local`).

## GitHub updates
See GITHUB.md: a GitHub Actions workflow (.github/workflows/build.yml) builds a release; **Admin tools > Update software from GitHub...** downloads it and sends it to the selected PCs.
