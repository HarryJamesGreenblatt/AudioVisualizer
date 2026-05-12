using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Services;

namespace AudioVisualizer;

public partial class MainWindow : Window
{
    private readonly AudioCaptureService _capture = new();
    private FftProcessingService _fft = new(fftSize: 1024, bandCount: 64);
    private bool _running;

    // Double-buffer: audio thread writes to _backBuffer then swaps;
    // UI thread reads from the latest completed snapshot via Interlocked.Exchange.
    private float[] _backBuffer = new float[64];
    private float[]? _readyBuffer;

    public MainWindow()
    {
        InitializeComponent();

        _capture.AudioDataAvailable += OnAudioData;

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
        if (!_running) return;

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
            StartStopButton.Content = "Start";
        }
        else
        {
            try
            {
                _capture.Start();
                int sr = _capture.WaveFormat?.SampleRate ?? 44100;
                _fft = new FftProcessingService(fftSize: 1024, bandCount: 64, sampleRate: sr);
                _running = true;
                StartStopButton.Content = "Stop";
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
    }

    private void RainToggle_Changed(object sender, RoutedEventArgs e)
        => Visualizer.SetRain(RainToggle.IsChecked == true);
}