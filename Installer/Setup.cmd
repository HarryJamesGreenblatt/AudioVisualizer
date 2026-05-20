@echo off
:: WavBall Installer — unblocks downloaded files and launches the GUI installer.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-ChildItem '%~dp0' | Unblock-File; & '%~dp0Install.ps1'"
