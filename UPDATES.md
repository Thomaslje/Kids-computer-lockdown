# Automatic updates (public repository, signed)

What you get:

- **Admin app**: when it starts it checks GitHub. If there is a new version, a window shows what changed with three choices: *Yes, update and open*, *No, open the program*, *Don't remind me about this version*. You can also make it install updates without asking (Admin tools > Updates).
- **Kiosk PCs**: can update themselves from GitHub, but only when the PC is idle (no tile program, no Chrome, no Steam game, nothing started from the admin app, no install or command running).
- Everything downloaded is **signature checked**. The kiosk agent runs as SYSTEM, so without this anyone who could change your release could run code on the kids' PCs.

## One-time setup (small steps)

1. **Make the repository public.** GitHub > your repo > Settings > scroll to *Danger zone* > *Change visibility* > Public. (Never put passwords or keys in the repo.)
2. **Create the signing key.** In the admin app: Admin tools > Updates > *Create update signing key...*
   - Click *Copy private key*.
   - Click *Save signing-public-key.txt...* and save it into your repo folder (the folder GitHub Desktop shows).
3. **Add the secret.** GitHub > repo > Settings > Secrets and variables > Actions > *New repository secret*. Name: `SIGNING_KEY`. Value: paste the private key. Then you can delete the copied key from the clipboard.
4. **Commit and push** `signing-public-key.txt` in GitHub Desktop.
5. **Run the workflow**: Actions > Build and release > Run workflow. The release now contains signed files and a change list (made from your commit messages, so write clear ones).
6. **Install the new admin app once by hand** (`KioskAdminSetup.exe` from the release). Old builds do not contain the key and cannot update themselves.
7. **Update the kids' PCs once** with Admin tools > *Update software from GitHub...*.
8. **Turn on kiosk self-update**: Admin tools > Updates > *Kiosk PCs update themselves...*, then *Push to selected PCs*.

From now on: change files > commit > push > run the workflow. Everything else follows by itself.

## Updating without admin rights

If the admin app is installed "for me only" (the installer asks), updating needs no admin rights at all. If it is installed for all users, the updater runs the installer and Windows asks for an administrator password. Kiosk PCs update as SYSTEM, so they never need a login.

## Good to know

- Never make a second signing key unless you must: apps built with the old key refuse updates signed with the new one.
- Turn on two-factor login on your GitHub account; whoever controls your repo controls your PCs.
- Local builds (`.\Build.ps1`) are marked "dev" and never check for updates.
- The kiosk PCs write a log to `C:\ProgramData\KioskSuite\autoupdate.log`.
