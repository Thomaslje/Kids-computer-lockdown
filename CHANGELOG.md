# Changelog

Releases are numbered `build-N` on GitHub. From now on the release notes are made automatically from the commit messages; this file records what the suite gained before that.

## Next release - Wake-on-LAN

- New "Wake up all (WOL)" button: sends a Wake-on-LAN signal to every PC in the list, then checks them again after 45 seconds. The admin app learns each PC's network card address (MAC) when it is online, so it needs software 2026-11-12-a on the PCs and one "Check now" while they are on. It works on the local network only. The PC's BIOS and network card must allow Wake-on-LAN (the tweak "Turn on Wake-on-LAN on the network card" handles the Windows side).

## Next release - create accounts, restore UAC

- New "Create account..." button: enter an account name and password, choose a group (Users, Administrators, Guests and the other built-in Windows groups) and push it to the selected PCs. If the account already exists, its password is changed and it is added to the group. The password is not stored in the admin app.
- New "Delete account..." button: reads the local accounts from every selected PC, lists them (built-in accounts and the Kiosk account are hidden), and deletes the account you pick on every PC that has it, optionally with its files. The last administrator on a PC is never deleted.
- New tweak "Turn User Account Control (UAC) on, default settings": restores the normal admin prompt on all accounts (restart needed).

## Next release - delete login files when a program is closed
- New "Delete when closed..." button for tiles (programs and Chrome): lists files or folders in the Kiosk account that are deleted when the program has been closed for 45 seconds, and when the kiosk starts. Includes a "Fill in the Roblox login" preset.
- Only things inside the Kiosk account's own profile can be deleted, and at least two folders deep.
- New "Chrome extensions..." button on the Kids' screen page: add extensions (Web Store address or ID) that are allowed on the kids' PCs and installed by Chrome itself.

## Earlier in this release - new look for the admin app
- Sidebar layout (PCs, Kids' screen, Admin tools, Tweaks, Updates, Passwords and key) with teal and orange colours.
- Overview cards (PCs online, offline, not on the newest software) and a coloured status dot for every PC.
- "New version available" banner when you chose "No" in the update popup.
- New app icon (shield with a screen) in the program, the installer and the taskbar.
- Lock app button in the sidebar (when a start password is set).
- The built-in KFUM logo is gone from the kids' screen.
- New "Logo..." button on the Kids' screen page: pick your own picture and choose the corner (lower right is the default). Push the setup to send it to the PCs.

## build-2 (2026-11-03) - signed automatic updates
- The admin app checks GitHub at start and shows a popup with the change list: update and open, open without updating, or don't remind me about this version.
- Option to install admin app updates automatically; works without administrator rights when the app is installed "for me only".
- Kiosk PCs can update themselves from GitHub, only while idle (no tile program, Chrome, Steam game, admin-started tool, install, command or file transfer running).
- All downloaded updates are signature checked (ECDSA); the signing key is created from the admin app (Admin tools > Updates).
- The repository is public; an access token is only needed for a private one.
- Chrome background mode is switched off on the kids' PCs so a lingering Chrome does not count as busy.

## build-1 (2026-11-03) - first GitHub release
- Build and release workflow on GitHub Actions: kids' PC update zip, admin zip and both installers.
- Admin app: update the kids' PCs from GitHub (Admin tools > Update software from GitHub).

## Before GitHub (all earlier versions)
**Kiosk and launcher**
- Fullscreen locked-down launcher with tiles for Steam games, programs (.exe) and Chrome.
- Windows service agent on every kids' PC, paired with the admin app by an encrypted key.
- "Close first" option on a tile (for example Roblox, so the launcher closes the game before starting it again) with a delay so repeated clicks do nothing.
- Update-proof program paths with wildcards (Roblox and games that change folder on every update).
- Chrome policies for kiosk use.
- Installers set the network profile to Private and turn on discovery and sharing, so the PC can be reached by the admin app.
- Kiosk shortcut, Defender exclusions and a remote uninstall that removes the kiosk, restarts the PC back to the normal login screen and removes it from the admin list.

**Admin app**
- Scan the network, add PCs, push the setup, unlock, restart and shut down remotely.
- Update software on the PCs from a folder, sending only changed files.
- Tweaks menu (power plan, screen and sleep never off, and others).
- Tools menu: share folder without password and file copying, silent installs of programs (such as the NVIDIA driver) with your own switches, start a program on the kids' PCs that is not a tile.
- Admin tools menu: command prompt, PowerShell, Task Manager and more opened on the PC's screen with administrator rights; console, processes, services, programs and screenshot.
- Start password for the admin app and a friendly password that opens a simple window with only restart and shut down.
- TeamViewer ID of every PC shown in both views.
- Status text at the bottom fades away after commands.
