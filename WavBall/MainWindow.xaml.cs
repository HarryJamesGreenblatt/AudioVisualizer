using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using WavBall.Configuration;
using WavBall.Models;
using WavBall.Services;

namespace WavBall;

public partial class MainWindow : Window
{
    private readonly AudioCaptureService _capture = new();
    private readonly MicCaptureService _mic = new();
    private FftProcessingService _fft    = new(fftSize: 1024, bandCount: 64);
    private FftProcessingService _micFft = new(fftSize: 1024, bandCount: 64);
    private bool _running;
    private bool _stopped;   // true after Stop is pressed; bars decay to zero. false = paused (bars freeze).
    private bool _micEnabled;

    // Double-buffer: audio thread writes to _backBuffer then swaps;
    // UI thread reads from the latest completed snapshot via Interlocked.Exchange.
    private float[]  _backBuffer    = new float[64];
    private float[]? _readyBuffer;
    private float[]  _micBackBuffer  = new float[64];
    private float[]? _micReadyBuffer;

    // Persistent last-known bands per source. Updated whenever a new frame arrives;
    // consulted every render frame. This decouples the visualizer's frame rate
    // from the two audio callbacks' independent cadences, eliminating the
    // "source A arrived this frame but source B didn't" magnitude cliff.
    private readonly float[] _lastLoopBands = new float[64];
    private readonly float[] _lastMicBands  = new float[64];

    // Scratch buffer for compositing loopback + mic bands without per-frame allocation.
    private readonly float[] _combinedBuffer = new float[64];

    // Mic-to-loopback gain calibration. Consumer mics typically read several times
    // hotter than the digital loopback level for the same perceived loudness, so
    // we attenuate before summing to keep neither source from dominating.
    private const float MicGain = 0.35f;

    // Pre-allocated zero array fed to the visualizer when capture is stopped,
    // so bars decay to the floor rather than freezing at their last values.
    private static readonly float[] _zeroBuffer = new float[64];
    private readonly SystemVolumeService _volume = new();
    private bool _suppressVolumeSync;
    private readonly RoundHistoryStore _history = new();
    private int _lastPanelStage = -1;
    private NowPlayingService? _nowPlaying;

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
        _mic.AudioDataAvailable     += OnMicData;

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

        // Start GSMTCS session tracker (fire-and-forget; hidden until data arrives).
        _ = InitNowPlayingAsync();

        // Wire transport atlas hover/down states. Per WMP9 BUTTONGROUP idiom, only the
        // hovered button lights up. We clip the hover/down overlay to each button's rect
        // (from transports_map.bmp) so only one button highlights at a time.
        Rect[] hitRects = [
            new(0, 0, 31, 30),   // PLAY   (#FF0000)
            new(34, 5, 22, 21),  // STOP   (#00FF00)
            new(61, 5, 22, 21),  // PREV   (#FFFF00)
            new(86, 5, 22, 21),  // NEXT   (#FA6A6A)
            new(113, 5, 22, 21), // MIC    (#79C666)
        ];
        var btns = new System.Windows.Controls.Primitives.ButtonBase[]
            { StartStopButton, StopButton, PrevTrackButton, NextTrackButton, MicToggle };
        for (int i = 0; i < btns.Length; i++)
        {
            var clip = new RectangleGeometry(hitRects[i]);
            clip.Freeze();
            var btn = btns[i];
            btn.MouseEnter += (_, _) =>
            {
                TransportsHover.Clip = clip;
                TransportsHover.Visibility = Visibility.Visible;
            };
            btn.MouseLeave += (_, _) =>
            {
                TransportsHover.Visibility = Visibility.Collapsed;
                TransportsDown.Visibility = Visibility.Collapsed;
            };
            btn.PreviewMouseDown += (_, _) =>
            {
                TransportsDown.Clip = clip;
                TransportsDown.Visibility = Visibility.Visible;
                TransportsHover.Visibility = Visibility.Collapsed;
            };
            btn.PreviewMouseUp += (_, _) =>
            {
                TransportsDown.Visibility = Visibility.Collapsed;
                TransportsHover.Clip = clip;
                TransportsHover.Visibility = Visibility.Visible;
            };
        }

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

    private void OnMicData(float[] samples)
    {
        int channels = _mic.WaveFormat?.Channels ?? 1;
        _micFft.Process(samples, channels, _micBackBuffer);
        var old = Interlocked.Exchange(ref _micReadyBuffer, _micBackBuffer);
        _micBackBuffer = old ?? new float[_micFft.BandCount];
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

        // Atomically grab the latest complete frame from each pipeline (if any),
        // and *copy into persistent state*. The visualizer then always reads
        // from persistent state — it doesn't matter which callback fired this frame.
        var loopFrame = Interlocked.Exchange(ref _readyBuffer,    null);
        var micFrame  = Interlocked.Exchange(ref _micReadyBuffer, null);

        if (loopFrame != null)
        {
            int n = Math.Min(_lastLoopBands.Length, loopFrame.Length);
            Buffer.BlockCopy(loopFrame, 0, _lastLoopBands, 0, n * sizeof(float));
        }
        if (micFrame != null && _micEnabled)
        {
            int n = Math.Min(_lastMicBands.Length, micFrame.Length);
            Buffer.BlockCopy(micFrame, 0, _lastMicBands, 0, n * sizeof(float));
        }

        // Additive composition with calibrated mic gain.
        // Both inputs are non-negative band magnitudes, so a plain sum is safe
        // and reads as "the bar shows whichever source has activity in this band,
        // and both together when both are active" — exactly the user's mental model.
        // The downstream AudioReactive component already normalises/smooths,
        // so we don't need to soft-clip here.
        if (_micEnabled)
        {
            int n = _combinedBuffer.Length;
            for (int i = 0; i < n; i++)
                _combinedBuffer[i] = _lastLoopBands[i] + MicGain * _lastMicBands[i];
            Visualizer.Tick(_combinedBuffer.AsSpan());
        }
        else
        {
            Visualizer.Tick(_lastLoopBands.AsSpan());
        }
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _capture.Stop();
            _running = false;
            _stopped = false;   // Pause — bars freeze, not zeroed
            Visualizer.PauseRoundTimer();
            PauseOverlay.Visibility = Visibility.Collapsed;
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
                PauseOverlay.Visibility = Visibility.Visible;
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
        _mic.Dispose();
        _volume.Dispose();
        _nowPlaying?.Dispose();
    }

    private void MicToggle_Changed(object sender, RoutedEventArgs e)
    {
        _micEnabled = MicToggle.IsChecked == true;
        MicLiveCaption.Visibility = _micEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (_micEnabled)
        {
            try
            {
                _mic.Start();
                int sr = _mic.WaveFormat?.SampleRate ?? 44100;
                _micFft = new FftProcessingService(fftSize: 1024, bandCount: 64, sampleRate: sr);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to start microphone capture:\n\n{ex.Message}\n\nCheck that a default input device is set in Windows Sound settings and that microphone access is allowed in Windows privacy settings.",
                    "Microphone Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                MicToggle.IsChecked = false;
                _micEnabled = false;
            }
        }
        else
        {
            _mic.Stop();
            _micReadyBuffer = null;
            Array.Clear(_lastMicBands);  // forget mic contribution immediately on disable
        }
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
        Array.Clear(_lastLoopBands);
        Array.Clear(_lastMicBands);
        Visualizer.PauseRoundTimer();
        PauseOverlay.Visibility = Visibility.Collapsed;
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

    // ── Now Playing ──────────────────────────────────────────────────────────

    private async Task InitNowPlayingAsync()
    {
        try
        {
            _nowPlaying = await NowPlayingService.CreateAsync();
            _nowPlaying.Changed += OnNowPlayingChanged;
            OnNowPlayingChanged();          // render current session immediately
        }
        catch { /* GSMTCS unavailable — section stays hidden */ }
    }

    // Changed fires on a background thread; marshal to UI thread.
    private void OnNowPlayingChanged() =>
        Dispatcher.BeginInvoke(new Action(async () => await UpdateNowPlayingUIAsync()));

    private async Task UpdateNowPlayingUIAsync()
    {
        if (_nowPlaying is not { HasSession: true })
        {
            NowPlayingHeader.Visibility  = Visibility.Collapsed;
            NowPlayingContent.Visibility = Visibility.Collapsed;
            MetadataText.Text = "";
            return;
        }

        NowPlayingHeader.Visibility  = Visibility.Visible;
        NowPlayingContent.Visibility = Visibility.Visible;
        NowPlayingApp.Text    = NowPlayingService.FormatAppName(_nowPlaying.SourceAppUserModelId);
        NowPlayingTitle.Text  = _nowPlaying.Title;
        NowPlayingArtist.Text = _nowPlaying.Artist;

        // Green metadata strip (Corona.wms-style green LED text)
        MetadataText.Text = string.IsNullOrEmpty(_nowPlaying.Artist)
            ? _nowPlaying.Title
            : $"{_nowPlaying.Artist} - {_nowPlaying.Title}";

        // App icon (synchronous — fast P/Invoke for unpackaged apps like Spotify.exe)
        NowPlayingAppIcon.Source = ExtractProcessIcon(_nowPlaying.SourceAppUserModelId);

        // Album art (async stream decode; continuation runs on UI thread via captured SyncContext)
        NowPlayingAlbumArt.Source = await LoadAlbumArtAsync(_nowPlaying.ThumbnailRef);
    }

    /// <summary>
    /// Extracts the 48×48 icon from the running process whose AUMID is an exe name.
    /// Returns null for packaged (Store) apps or when the process isn't found.
    /// </summary>
    private static ImageSource? ExtractProcessIcon(string aumid)
    {
        if (!aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        string procName = Path.GetFileNameWithoutExtension(aumid);
        var procs = Process.GetProcessesByName(procName);
        if (procs.Length == 0) return null;
        try
        {
            string? path = procs[0].MainModule?.FileName;
            if (path is null) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(48, 48));
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { foreach (var p in procs) p.Dispose(); }
    }

    /// <summary>
    /// Decodes the GSMTCS thumbnail into a frozen BitmapImage on the UI thread.
    /// </summary>
    private static async Task<ImageSource?> LoadAlbumArtAsync(
        Windows.Storage.Streams.IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef is null) return null;
        try
        {
            using var winrtStream = await thumbRef.OpenReadAsync();
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = winrtStream.AsStreamForRead();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
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