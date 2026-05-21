@echo off
:: WavBall Installer — unblocks downloaded files and launches the GUI installer.
:: -WindowStyle Hidden suppresses the terminal window entirely.
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "Get-ChildItem '%~dp0' | Unblock-File; & '%~dp0Install.ps1'"
