# WavBall Installer — imports signing certificate (elevated) then installs the MSIX package.
# Usage: Right-click → "Run with PowerShell"  OR  powershell -ExecutionPolicy Bypass -File Install.ps1

param([switch]$Elevated)

#--- Self-elevate if not admin ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "Requesting administrator privileges..." -ForegroundColor Yellow
    $argList = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -Elevated' -f $PSCommandPath
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait
    }
    catch {
        Write-Host "ERROR: Administrator privileges are required to install the certificate." -ForegroundColor Red
        Write-Host "Please right-click and select 'Run with PowerShell' or run from an admin terminal." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
    # After the elevated process finishes, install the MSIX (no elevation needed)
    $msix = Join-Path $PSScriptRoot "WavBall.msix"
    if (Test-Path $msix) {
        Write-Host "Installing WavBall package..." -ForegroundColor Cyan
        try {
            Add-AppxPackage -Path $msix -ForceApplicationShutdown
            Write-Host "WavBall installed successfully!" -ForegroundColor Green
        }
        catch {
            Write-Host "Package install failed: $_" -ForegroundColor Red
            Write-Host "Trying to open with App Installer instead..." -ForegroundColor Yellow
            Start-Process $msix
        }
    }
    else {
        Write-Host "WavBall.msix not found in $PSScriptRoot" -ForegroundColor Red
    }
    Read-Host "Press Enter to exit"
    exit 0
}

#--- Running elevated: import the certificate ---
$cert = Join-Path $PSScriptRoot "WavBall.cer"
if (-not (Test-Path $cert)) {
    Write-Host "ERROR: Certificate file not found: $cert" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Installing WavBall certificate into TrustedPeople store..." -ForegroundColor Cyan
try {
    Import-Certificate -FilePath $cert -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
    Write-Host "Certificate installed successfully." -ForegroundColor Green
}
catch {
    Write-Host "ERROR: Certificate installation failed: $_" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# If invoked directly as admin (not via self-elevation), also install the package
if ($Elevated) {
    exit 0
}

$msix = Join-Path $PSScriptRoot "WavBall.msix"
if (Test-Path $msix) {
    Write-Host "Installing WavBall package..." -ForegroundColor Cyan
    Add-AppxPackage -Path $msix -ForceApplicationShutdown
    Write-Host "WavBall installed successfully!" -ForegroundColor Green
}
Read-Host "Press Enter to exit"
