@echo off
:: WavBall Installer — imports signing certificate and launches App Installer.
:: Self-elevates if not already running as admin.

net session >nul 2>&1 || (
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Installing WavBall certificate...
certutil -addstore TrustedPeople "%~dp0WavBall.cer" >nul 2>&1
if %errorlevel% neq 0 (
    echo Certificate installation failed. Please run as Administrator.
    pause
    exit /b 1
)

echo Launching WavBall installer...
start "" "%~dp0WavBall.msix"
