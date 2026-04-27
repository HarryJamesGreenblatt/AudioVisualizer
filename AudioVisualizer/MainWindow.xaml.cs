using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Services;
using AudioVisualizer.Processing;

namespace AudioVisualizer;

public partial class MainWindow : Window
{
    private readonly AudioCaptureService _capture = new();
    private FftProcessor _fft = new(fftSize: 1024, bandCount: 64);
    private bool _running;

    // Double-buffer: audio thread writes to _backBuffer then swaps;
    // UI thread reads from the latest completed snapshot via Interlocked.Exchange.
    // This guarantees the render side never sees a half-written frame.
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
        // Grab whatever buffer the UI side hands back (or null) so we can reuse it next time.
        _fft.Process(samples, channels, _backBuffer);
        var old = Interlocked.Exchange(ref _readyBuffer, _backBuffer);
        _backBuffer = old ?? new float[_fft.BandCount];
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running) return;

        // Atomically grab the latest complete frame (if one is available)
        var frame = Interlocked.Exchange(ref _readyBuffer, null);
        if (frame != null)
            Visualizer.Update(frame);
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
                // Rebuild FftProcessor with the device's actual sample rate
                int sr = _capture.WaveFormat?.SampleRate ?? 44100;
                _fft = new FftProcessor(fftSize: 1024, bandCount: 64, sampleRate: sr);
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
}