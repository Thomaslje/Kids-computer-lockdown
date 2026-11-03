# Updating over the internet with GitHub

GitHub builds the software for you and keeps it in a "release". In Kiosk Admin, **Admin tools > Update software from GitHub...** downloads the newest release and sends it to the selected PCs (only changed files are sent, exactly like the normal "Update software...").

## One-time setup

1. **Create a private repository** on github.com (New repository, Private), for example `kiosk-suite`.
2. **Upload this project to it.** On your PC, in the KioskSuite folder (needs Git for Windows):
   ```
   git init
   git add .
   git commit -m "First version"
   git branch -M main
   git remote add origin https://github.com/<your-name>/kiosk-suite.git
   git push -u origin main
   ```
   (The `.gitignore` keeps build folders out.)
3. **Make the first release.** On your PC:
   ```
   git tag v2026.11.03
   git push origin v2026.11.03
   ```
   On GitHub, open the **Actions** tab: the "Build and release" job runs for about 5-10 minutes. When it is green, the **Releases** page (right side of the repository) has `kiosk-update.zip`, `admin.zip`, `KioskSetup.exe` and `KioskAdminSetup.exe`.
4. **Create an access token** for the admin app: GitHub > your picture > Settings > Developer settings > Personal access tokens > **Fine-grained tokens** > Generate new token.
   - Repository access: *Only select repositories* > your kiosk repository.
   - Permissions > Repository permissions > **Contents: Read-only**.
   - Set an expiry (for example one year) and copy the token (shown once).
5. In Kiosk Admin: **Admin tools > GitHub settings...** and enter `<your-name>/kiosk-suite` and the token. The token is stored encrypted for your Windows account.

## Every time you change the software

1. Push the changed files: `git add .`, `git commit -m "what changed"`, `git push`.
2. Make a new release with a new tag (the name just has to start with v and be new):
   ```
   git tag v2026.11.04
   git push origin v2026.11.04
   ```
3. Wait for the green tick in the Actions tab.
4. In Kiosk Admin: select the PCs > **Admin tools > Update software from GitHub...** > Yes.

The admin app itself is not updated this way: download `KioskAdminSetup.exe` from the release and run it on your PC.

## Notes
- The kids' PCs never talk to GitHub. Only your admin app does, then sends the files to the PCs on your network.
- If the token expires, make a new one and enter it again in GitHub settings.
- If you forget a step: the Actions tab shows the error log of the build.
