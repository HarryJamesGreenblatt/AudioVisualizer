@echo off
:: WavBall Installer — thin wrapper that launches Install.ps1.
:: Double-click this file, or right-click Install.ps1 → "Run with PowerShell".
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
