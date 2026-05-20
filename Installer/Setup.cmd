@echo off
:: WavBall Installer — launches the GUI installer with no visible console.
powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Install.ps1"
