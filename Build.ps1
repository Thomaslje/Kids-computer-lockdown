# Builds everything. Needs the free .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0).
# Optional: Inno Setup 6 (https://jrsoftware.org/isinfo.php) turns the result into real installer .exe files.
param([switch]$Quick, [string]$ReleaseTag = "dev")   # -Quick: build only the kids' PC software (faster): no admin app, no installers
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
if ($Quick) { Remove-Item "$dist\kiosk", "$dist\build\Agent", "$dist\build\Launcher" -Recurse -Force -ErrorAction SilentlyContinue }
else { Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue }

# Make sure the .sln (for Visual Studio) lists every project
foreach ($p in "Shared","Agent","Launcher","Admin") {
    if (-not (dotnet sln "$root\KioskSuite.sln" list | Select-String "$p.csproj")) { dotnet sln "$root\KioskSuite.sln" add "$root\$p\$p.csproj" | Out-Null }
}

# Built-in update settings: the release name (so apps know their own version) and the public signing key
$extra = @("-p:ReleaseTag=$ReleaseTag")
$keyFile = Join-Path $root "signing-public-key.txt"
if (Test-Path $keyFile) { $extra += "-p:SigningPublicKey=$((Get-Content $keyFile -Raw).Trim())" }
else { Write-Host "No signing-public-key.txt: this build will not install downloaded updates." -ForegroundColor Yellow }

$projects = if ($Quick) { @("Agent", "Launcher") } else { @("Agent", "Launcher", "Admin") }
foreach ($p in $projects) {
    Write-Host "Building $p ..." -ForegroundColor Cyan
    dotnet publish "$root\$p\$p.csproj" -c Release -r win-x64 --self-contained true @extra -o "$dist\build\$p"
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $p" }
}

# Package for the kids' PCs
$k = Join-Path $dist "kiosk"
New-Item -ItemType Directory -Force $k | Out-Null
Copy-Item "$dist\build\Agent"    "$k\Agent"    -Recurse
Copy-Item "$dist\build\Launcher" "$k\Launcher" -Recurse
Copy-Item "$root\scripts\*" $k

if ($Quick) {
    Write-Host "`nDone. In Kiosk Admin: select the PCs > Update software... > pick: $k" -ForegroundColor Green
    return
}

# Package for the admin PC
Copy-Item "$dist\build\Admin" "$dist\admin" -Recurse
Copy-Item "$root\scripts\Setup-Network.ps1" "$dist\admin"

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) {
    & $iscc "$root\installer\kiosk.iss"
    & $iscc "$root\installer\admin.iss"
    Write-Host "`nDone. Installers: dist\KioskSetup.exe (kids' PCs) and dist\KioskAdminSetup.exe (your PC)." -ForegroundColor Green
} else {
    Write-Host "`nDone (Inno Setup not found, so no single-file installers)." -ForegroundColor Yellow
    Write-Host "  Admin PC : run dist\admin\KioskAdmin.exe"
    Write-Host "  Kids' PCs: copy dist\kiosk to the PC, then run Install.cmd -Key `"<pairing key>`" as Administrator"
}
