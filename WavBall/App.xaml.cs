using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WavBall;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Catch anything thrown on the UI dispatcher (XAML load, event handlers, etc.)
        DispatcherUnhandledException += (_, args) =>
        {
            ReportCrash("Dispatcher", args.Exception);
            args.Handled = true;   // keep the process alive so the user sees the message box
        };

        // Catch anything thrown on background / pool threads
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportCrash("AppDomain", args.ExceptionObject as Exception);

        // Catch unobserved task exceptions
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportCrash("Task", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static void ReportCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WavBall");
            Directory.CreateDirectory(dir);

            var logPath = Path.Combine(dir, "crash.log");
            var entry = $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry);

            MessageBox.Show(
                $"WavBall crashed.\n\nA log has been written to:\n{logPath}\n\n{ex?.GetType().Name}: {ex?.Message}",
                "WavBall — Unhandled Exception",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // last-ditch: never let the crash handler itself crash
        }
    }
}

