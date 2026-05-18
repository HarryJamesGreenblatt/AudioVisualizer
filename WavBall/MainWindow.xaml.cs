using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WavBall.Configuration;
using WavBall.Models;
using WavBall.Services;

namespace WavBall;

public partial class MainWindow : Window
{
    private readonly AudioCaptureService _capture = new();
    private FftProcessingService _fft = new(fftSize: 1024, bandCount: 64);
    private bool _running;
    private bool _stopped;   // true after Stop is pressed; bars decay to zero. false = paused (bars freeze).

    // Double-buffer: audio thread writes to _backBuffer then swaps;
    // UI thread reads from the latest completed snapshot via Interlocked.Exchange.
    private float[] _backBuffer = new float[64];
    private float[]? _readyBuffer;
    // Pre-allocated zero array fed to the visualizer when capture is stopped,
    // so bars decay to the floor rather than freezing at their last values.
    private static readonly float[] _zeroBuffer = new float[64];
    private readonly SystemVolumeService _volume = new();
    private bool _suppressVolumeSync;
    private readonly RoundHistoryStore _history = new();
    private int _lastPanelStage = -1;

    // Real-world display values per stage (same order as BallPreset.Stages).
    private static readonly (string Mass, string Radius)[] _ballRealWorld =
    [
        ("100 g",  "22 cm"),    // Beach Ball
        ("40 g",   "3.0 cm"),   // Racquetball
        ("58 g",   "3.3 cm"),   // Tennis Ball
        ("430 g",  "11 cm"),    // Soccer Ball
        ("620 g",  "12 cm"),    // Basketball
        ("142 g",  "3.7 cm"),   // Baseball
        ("4.3 kg", "10.9 cm"),  // Bowling Ball
    ];

    public MainWindow()
    {
        InitializeComponent();

        _capture.AudioDataAvailable += OnAudioData;

        // Initial volume slider sync + subscribe for external changes (taskbar / hardware keys).
        VolumeSlider.Value = _volume.Volume;
        _volume.VolumeChangedExternally += v => Dispatcher.BeginInvoke(() =>
        {
            _suppressVolumeSync = true;
            try { VolumeSlider.Value = v; }
            finally { _suppressVolumeSync = false; }
        });

        // Side panel wiring.
        RoundHistoryList.ItemsSource = _history.Records;
        Visualizer.RoundCompleted += OnRoundCompleted;

        // Hook into the WPF compositor for smooth 60fps redraws
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnAudioData(float[] samples)
    {
        int channels = _capture.WaveFormat?.Channels ?? 2;

        // Write into the back buffer, then atomically publish it.
        _fft.Process(samples, channels, _backBuffer);
        var old = Interlocked.Exchange(ref _readyBuffer, _backBuffer);
        _backBuffer = old ?? new float[_fft.BandCount];
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Refresh timer in the side panel (ticks even when audio is off).
        TimerDisplay.Text = Visualizer.LedText;

        // Refresh ball-info section only when the stage changes (not every frame).
        int stage = Visualizer.CurrentStage;
        if (stage != _lastPanelStage)
        {
            _lastPanelStage = stage;
            var p = BallPreset.Stages[Math.Min(stage, BallPreset.Stages.Length - 1)];
            BallNameDisplay.Text    = p.Name;
            BallIcon.Source         = BallSpriteImage(p.Kind);
            var (rwMass, rwRadius)  = _ballRealWorld[Math.Min(stage, _ballRealWorld.Length - 1)];
            StatMass.Text           = rwMass;
            StatRestitution.Text    = p.Restitution.ToString("F2");
            StatRadius.Text         = rwRadius;
        }

        if (!_running)
        {
            if (_stopped)
                Visualizer.Tick(_zeroBuffer.AsSpan());  // Stop: bars decay to zero each frame
            // Paused: no tick — bars freeze at their last values
            return;
        }

        // Atomically grab the latest complete frame (if one is available)
        var frame = Interlocked.Exchange(ref _readyBuffer, null);

        // Pass actual data as a span, or Empty when no new audio arrived.
        // Empty span causes AudioReactive to skip (bars retain last values),
        // while physics still ticks — fixing both strobe and frozen-peaks bugs.
        Visualizer.Tick(frame != null ? frame.AsSpan() : ReadOnlySpan<float>.Empty);
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _capture.Stop();
            _running = false;
            _stopped = false;   // Pause — bars freeze, not zeroed
            Visualizer.PauseRoundTimer();
            PlayGlyph.Visibility = Visibility.Visible;
            PauseGlyph.Visibility = Visibility.Collapsed;
            StartStopButton.ToolTip = "Play";
        }
        else
        {
            try
            {
                _capture.Start();
                int sr = _capture.WaveFormat?.SampleRate ?? 44100;
                _fft = new FftProcessingService(fftSize: 1024, bandCount: 64, sampleRate: sr);
                _running = true;
                _stopped = false;   // resuming from either Pause or Stop
                Visualizer.ResumeRoundTimer();
                PlayGlyph.Visibility = Visibility.Collapsed;
                PauseGlyph.Visibility = Visibility.Visible;
                StartStopButton.ToolTip = "Pause";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to start audio capture:\n\n{ex.Message}\n\nMake sure a default audio output device is set in Windows Sound settings.",
                    "Audio Device Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _capture.Dispose();
        _volume.Dispose();
    }

    private void PrevTrackButton_Click(object sender, RoutedEventArgs e) => MediaKeyService.PrevTrack();

    private void NextTrackButton_Click(object sender, RoutedEventArgs e) => MediaKeyService.NextTrack();

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_running) return;
        _capture.Stop();
        _running = false;
        _stopped = true;   // Stop — bars will decay to zero
        _readyBuffer = null;
        Visualizer.PauseRoundTimer();
        PlayGlyph.Visibility = Visibility.Visible;
        PauseGlyph.Visibility = Visibility.Collapsed;
        StartStopButton.ToolTip = "Play";
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressVolumeSync) return;
        _volume.Volume = (float)e.NewValue;
    }

    private void OnRoundCompleted(BallKind kind, string ballName, TimeSpan elapsed)
    {
        _history.Add(kind, ballName, elapsed);
    }

    private static BitmapImage BallSpriteImage(BallKind kind)
    {
        var fileName = kind switch
        {
            BallKind.BeachBall   => "beach-ball.png",
            BallKind.Racquetball => "racquetball.png",
            BallKind.TennisBall  => "tennis-ball.png",
            BallKind.SoccerBall  => "soccer-ball.png",
            BallKind.Basketball  => "basketball.png",
            BallKind.Baseball    => "baseball.png",
            BallKind.BowlingBall => "bowling-ball.png",
            _                    => "beach-ball.png",
        };
        var uri = new Uri($"pack://application:,,,/Assets/Generated/{fileName}", UriKind.Absolute);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = uri;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void RainToggle_Changed(object sender, RoutedEventArgs e)
        => Visualizer.SetRain(RainToggle.IsChecked == true);

    // ===== Custom window chrome (WindowChrome takes over from native title bar) =====

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Windows applies invisible chrome padding when a WindowChrome'd window is maximized,
    /// causing the outer chassis border to bleed off-screen. Compensate by inset margin
    /// while maximized, and swap the maximize glyph for a "restore" glyph.
    /// </summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            OuterChassis.Margin = new Thickness(7);
            MaximizeGlyph.Data = (Geometry)FindResource("WmpRestoreGeometry");
            MaximizeButton.ToolTip = "Restore";
        }
        else
        {
            OuterChassis.Margin = new Thickness(0);
            MaximizeGlyph.Data = (Geometry)FindResource("WmpMaximizeGeometry");
            MaximizeButton.ToolTip = "Maximize";
        }
    }
}