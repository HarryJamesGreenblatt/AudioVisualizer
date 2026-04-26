using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer;

public partial class MainWindow : Window
{
    private readonly AudioCaptureService _capture = new();
    private FftProcessor _fft = new(fftSize: 2048, bandCount: 64);
    private readonly float[] _bands = new float[64];
    private bool _running;
    private volatile bool _audioReceived;

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
        _fft.Process(samples, channels, _bands);
        _audioReceived = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_running) return;

        // If no audio data arrived since last frame, decay bands toward zero
        if (!_audioReceived)
        {
            for (int i = 0; i < _bands.Length; i++)
                _bands[i] *= 0.85f;
        }
        _audioReceived = false;

        Visualizer.Update(_bands);
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
                _fft = new FftProcessor(fftSize: 2048, bandCount: 64, sampleRate: sr);
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