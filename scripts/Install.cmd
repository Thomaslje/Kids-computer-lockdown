@echo off
rem Right-click > Run as administrator.   Usage: Install.cmd -Key "<pairing key>"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Kiosk.ps1" %*
pause
