namespace WavBall.Installer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Silent cert-only mode: called elevated by the main instance
        if (args.Length > 0 && args[0] == "--cert-only")
        {
            InstallCertificate();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }

    internal static int InstallCertificate()
    {
        try
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream("WavBall.cer");
            if (stream == null) return 1;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(bytes);
            using var store = new System.Security.Cryptography.X509Certificates.X509Store(
                System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople,
                System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
            store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
            store.Add(cert);
            store.Close();
            return 0;
        }
        catch { return 1; }
    }
}