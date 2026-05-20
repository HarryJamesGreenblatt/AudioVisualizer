using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace WavBall.Installer;

/// <summary>
/// InstallerForm is a simple Windows Forms UI that guides the user through installing WavBall.
/// </summary>
internal sealed class InstallerForm : Form
{
    // ── WMP9 Corona palette ──
    static readonly Color SteelDark = Color.FromArgb(56, 88, 160);
    static readonly Color SteelMid  = Color.FromArgb(58, 106, 168);
    static readonly Color Highlight = Color.FromArgb(184, 220, 244);
    static readonly Color Amber     = Color.FromArgb(255, 138, 31);
    static readonly Color Chrome    = Color.FromArgb(228, 238, 248);
    static readonly Color Shadow    = Color.FromArgb(21, 58, 110);
    static readonly Color DarkNavy  = Color.FromArgb(20, 36, 62);

    readonly Label _statusLabel;
    readonly ProgressBar _progressBar;
    readonly Button _actionButton;
    bool _done;

    /// <summary>
    /// Constructs the installer form, setting up the UI elements and layout.
    /// </summary>
    public InstallerForm()
    {
        Text = "WavBall Setup";
        ClientSize = new Size(520, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = DarkNavy;

        // Try to set form icon from embedded resource
        try
        {
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Icon.png");
            if (iconStream != null)
            {
                using var bmp = new Bitmap(iconStream);
                Icon = Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch { }

        // ── Top chrome bar ──
        var topBar = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(520, 90),
            BackColor = SteelDark
        };
        Controls.Add(topBar);

        // App icon in chrome bar
        try
        {
            using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("Icon.png");
            if (iconStream != null)
            {
                var logoPic = new PictureBox
                {
                    Image = new Bitmap(iconStream),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(48, 48),
                    Location = new Point(18, 18),
                    BackColor = Color.Transparent
                };
                topBar.Controls.Add(logoPic);
            }
        }
        catch { }

        int titleX = 76;

        var titleLabel = new Label
        {
            Text = "Windows WavBall",
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Chrome,
            AutoSize = true,
            Location = new Point(titleX, 8),
            BackColor = Color.Transparent
        };
        topBar.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = "Setup",
            Font = new Font("Segoe UI", 11f),
            ForeColor = Highlight,
            AutoSize = true,
            Location = new Point(titleX + 2, 52),
            BackColor = Color.Transparent
        };
        topBar.Controls.Add(subtitleLabel);

        // ── Status label ──
        _statusLabel = new Label
        {
            Text = "Click Install to set up WavBall on this PC.",
            Font = new Font("Segoe UI", 11f),
            ForeColor = Highlight,
            Size = new Size(480, 24),
            Location = new Point(20, 108)
        };
        Controls.Add(_statusLabel);

        // ── Progress bar ──
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 145),
            Size = new Size(480, 22),
            Minimum = 0,
            Maximum = 100,
            Value = 0
        };
        Controls.Add(_progressBar);

        // ── Bottom panel ──
        var bottomPanel = new Panel
        {
            Location = new Point(0, 190),
            Size = new Size(520, 90),
            BackColor = Shadow
        };
        Controls.Add(bottomPanel);

        // Amber accent line
        var accentLine = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(520, 2),
            BackColor = Amber
        };
        bottomPanel.Controls.Add(accentLine);

        // ── Install button ──
        _actionButton = new Button
        {
            Text = "Install",
            Font = new Font("Segoe UI Semibold", 12f),
            Size = new Size(150, 42),
            Location = new Point(185, 20),
            BackColor = SteelMid,
            ForeColor = Chrome,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _actionButton.FlatAppearance.BorderColor = Highlight;
        _actionButton.FlatAppearance.BorderSize = 1;
        _actionButton.Click += OnActionClick;
        bottomPanel.Controls.Add(_actionButton);
    }

    /// <summary>
    /// Updates the status label and progress bar with the given text and percentage.
    /// </summary>
    /// <param name="text"></param>
    /// <param name="pct"></param>
    void UpdateStatus(string text, int pct)
    {
        _statusLabel.Text = text;
        _progressBar.Value = pct;
        Refresh();
    }

    /// <summary>
    /// Event handler for the action button click. This method performs the installation steps.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    async void OnActionClick(object? sender, EventArgs e)
    {
        if (_done) { Close(); return; }

        _actionButton.Enabled = false;

        // Step 1: Check if cert is already trusted
        bool certNeeded = !IsCertTrusted();

        if (certNeeded)
        {
            UpdateStatus("Installing certificate (admin required)...", 15);

            // Launch self as elevated with --cert-only
            var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--cert-only",
                Verb = "runas",
                UseShellExecute = true
            };

            try
            {
                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode != 0)
                    {
                        UpdateStatus("Certificate installation failed.", 0);
                        _actionButton.Enabled = true;
                        return;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC
                UpdateStatus("Installation cancelled.", 0);
                _actionButton.Enabled = true;
                return;
            }

            UpdateStatus("Certificate installed.", 40);
        }
        else
        {
            UpdateStatus("Certificate already trusted.", 40);
        }

        // Step 2: Find WavBall.msix next to the exe
        var msixPath = Path.Combine(AppContext.BaseDirectory, "WavBall.msix");
        if (!File.Exists(msixPath))
        {
            UpdateStatus("ERROR: WavBall.msix not found next to setup exe.", 40);
            _actionButton.Enabled = true;
            return;
        }

        UpdateStatus("Installing WavBall...", 60);
        Refresh();

        // Step 3: Install via Add-AppxPackage (PowerShell)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Add-AppxPackage -Path '{msixPath}' -ForceApplicationShutdown\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            var proc = Process.Start(psi);
            if (proc != null)
            {
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                    throw new Exception(stderr.Trim());
            }

            UpdateStatus("WavBall installed successfully!  Find it in your Start Menu.", 100);
        }
        catch
        {
            // Fallback: open MSIX in App Installer
            UpdateStatus("Opening App Installer...", 80);
            Refresh();
            Process.Start(new ProcessStartInfo(msixPath) { UseShellExecute = true });
            UpdateStatus("Follow the App Installer prompts to complete setup.", 100);
        }

        _done = true;
        _actionButton.Text = "Close";
        _actionButton.Enabled = true;
    }

    /// <summary>
    /// Determines if the WavBall certificate is already trusted on this machine by checking the Trusted People store for a matching thumbprint.
    /// </summary>
    /// <returns></returns>
    static bool IsCertTrusted()
    {
        try
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("WavBall.cer");
            if (stream == null) return false;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var cert = X509CertificateLoader.LoadCertificate(bytes);
            using var store = new X509Store(StoreName.TrustedPeople, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false);
            return found.Count > 0;
        }
        catch { return false; }
    }
}
