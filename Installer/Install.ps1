# WavBall Installer — GUI-based installer using WinForms.
# Imports signing certificate (elevated) then installs the MSIX package.
# Usage: Called from Setup.cmd or right-click → "Run with PowerShell"

param([switch]$CertOnly)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
try { [System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::SystemAware) } catch { }

# ── If launched with -CertOnly, silently import cert and exit (runs elevated) ──
if ($CertOnly) {
    $cert = Join-Path $PSScriptRoot "WavBall.cer"
    if (Test-Path $cert) {
        try {
            Import-Certificate -FilePath $cert -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
            exit 0
        } catch { exit 1 }
    }
    exit 1
}

# ── GUI Setup — WMP9 Corona theme ──
$form = New-Object System.Windows.Forms.Form
$form.Text = "WavBall Setup"
$form.ClientSize = New-Object System.Drawing.Size(640, 320)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(20, 36, 62)

# WMP9 Corona palette
$steelDark   = [System.Drawing.Color]::FromArgb(56, 88, 160)    # #3858A0
$steelMid    = [System.Drawing.Color]::FromArgb(58, 106, 168)   # #3A6AA8
$highlight   = [System.Drawing.Color]::FromArgb(184, 220, 244)  # #B8DCF4
$amber       = [System.Drawing.Color]::FromArgb(255, 138, 31)   # #FF8A1F
$chrome      = [System.Drawing.Color]::FromArgb(228, 238, 248)  # #E4EEF8
$shadow      = [System.Drawing.Color]::FromArgb(21, 58, 110)    # #153A6E

# Top chrome bar (mimics title bar gradient)
$topBar = New-Object System.Windows.Forms.Panel
$topBar.Location = New-Object System.Drawing.Point(0, 0)
$topBar.Size = New-Object System.Drawing.Size(640, 100)
$topBar.BackColor = $steelDark
$form.Controls.Add($topBar)

# Load icon for the title bar
$iconPath = Join-Path $PSScriptRoot "WavBall.cer"  # placeholder — check for icon
$logoPath = Join-Path $PSScriptRoot "..\WavBall\Assets\Images\Icon.png"
if (-not (Test-Path $logoPath)) { $logoPath = Join-Path $PSScriptRoot "Icon.png" }

if (Test-Path $logoPath) {
    $logoImage = [System.Drawing.Image]::FromFile((Resolve-Path $logoPath))
    $logoPic = New-Object System.Windows.Forms.PictureBox
    $logoPic.Image = $logoImage
    $logoPic.SizeMode = "Zoom"
    $logoPic.Size = New-Object System.Drawing.Size(42, 42)
    $logoPic.Location = New-Object System.Drawing.Point(16, 16)
    $logoPic.BackColor = [System.Drawing.Color]::Transparent
    $topBar.Controls.Add($logoPic)
    $titleX = 66
} else {
    $titleX = 20
}

$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "Windows WavBall"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 24, [System.Drawing.FontStyle]::Bold)
$titleLabel.ForeColor = $chrome
$titleLabel.AutoSize = $true
$titleLabel.Location = New-Object System.Drawing.Point($titleX, 12)
$topBar.Controls.Add($titleLabel)

$subtitleLabel = New-Object System.Windows.Forms.Label
$subtitleLabel.Text = "Setup  -  v2.2.0"
$subtitleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 11)
$subtitleLabel.ForeColor = $highlight
$subtitleLabel.AutoSize = $true
$subtitleLabel.Location = New-Object System.Drawing.Point($titleX, 58)
$topBar.Controls.Add($subtitleLabel)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "Click Install to set up WavBall on this PC."
$statusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 12)
$statusLabel.ForeColor = $highlight
$statusLabel.Size = New-Object System.Drawing.Size(600, 28)
$statusLabel.Location = New-Object System.Drawing.Point(20, 120)
$form.Controls.Add($statusLabel)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Location = New-Object System.Drawing.Point(20, 160)
$progressBar.Size = New-Object System.Drawing.Size(600, 24)
$progressBar.Style = "Continuous"
$progressBar.Minimum = 0
$progressBar.Maximum = 100
$progressBar.Value = 0
$form.Controls.Add($progressBar)

# Bottom panel with button
$bottomPanel = New-Object System.Windows.Forms.Panel
$bottomPanel.Location = New-Object System.Drawing.Point(0, 205)
$bottomPanel.Size = New-Object System.Drawing.Size(640, 125)
$bottomPanel.BackColor = $shadow
$form.Controls.Add($bottomPanel)

# Thin amber accent line
$accentLine = New-Object System.Windows.Forms.Panel
$accentLine.Location = New-Object System.Drawing.Point(0, 0)
$accentLine.Size = New-Object System.Drawing.Size(640, 2)
$accentLine.BackColor = $amber
$bottomPanel.Controls.Add($accentLine)

$installButton = New-Object System.Windows.Forms.Button
$installButton.Text = "Install"
$installButton.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 12)
$installButton.Size = New-Object System.Drawing.Size(160, 44)
$installButton.Location = New-Object System.Drawing.Point(240, 22)
$installButton.BackColor = $steelMid
$installButton.ForeColor = $chrome
$installButton.FlatStyle = "Flat"
$installButton.FlatAppearance.BorderColor = $highlight
$installButton.FlatAppearance.BorderSize = 1
$installButton.Cursor = [System.Windows.Forms.Cursors]::Hand
$bottomPanel.Controls.Add($installButton)

# ── Install logic ──
function Update-Status($text, $pct) {
    $statusLabel.Text = $text
    $progressBar.Value = $pct
    $form.Refresh()
}

$installButton.Add_Click({
    $installButton.Enabled = $false

    # Step 1: Import certificate (elevated)
    Update-Status "Installing certificate..." 20
    $certPath = Join-Path $PSScriptRoot "WavBall.cer"
    if (-not (Test-Path $certPath)) {
        Update-Status "ERROR: WavBall.cer not found" 0
        $installButton.Enabled = $true
        return
    }

    $argList = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -CertOnly' -f $PSCommandPath
    try {
        $proc = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
        if ($proc.ExitCode -ne 0) { throw "Certificate import failed" }
    } catch {
        Update-Status "Certificate install cancelled or failed." 0
        $installButton.Enabled = $true
        return
    }

    Update-Status "Certificate installed" 40

    # Step 2: Install MSIX package
    $msixPath = Join-Path $PSScriptRoot "WavBall.msix"
    if (-not (Test-Path $msixPath)) {
        Update-Status "ERROR: WavBall.msix not found" 40
        $installButton.Enabled = $true
        return
    }

    Update-Status "Installing WavBall..." 60
    $form.Refresh()

    try {
        Add-AppxPackage -Path $msixPath -ForceApplicationShutdown
        Update-Status "WavBall installed successfully!" 100
        $installButton.Text = "Done"
        $installButton.Enabled = $true
        $script:done = $true
    } catch {
        Update-Status "Falling back to App Installer..." 80
        $form.Refresh()
        Start-Process $msixPath
        Update-Status "App Installer opened." 100
        $installButton.Text = "Close"
        $installButton.Enabled = $true
        $script:done = $true
    }
})

$closeHandler = { $form.Close() }
$script:done = $false
$installButton.Add_Click({
    if ($script:done) { $form.Close() }
})

# ── Show form ──
$form.ShowDialog() | Out-Null

$msix = Join-Path $PSScriptRoot "WavBall.msix"
if (Test-Path $msix) {
    Write-Host "Installing WavBall package..." -ForegroundColor Cyan
    Add-AppxPackage -Path $msix -ForceApplicationShutdown
    Write-Host "WavBall installed successfully!" -ForegroundColor Green
}
Read-Host "Press Enter to exit"
