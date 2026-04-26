using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioVisualizer;

/// <summary>
/// Captures system audio output via WASAPI loopback.
/// Works with Spotify, browsers, or any audio playing through the default output device.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private bool _disposed;

    /// <summary>
    /// Fired on the capture thread with each new buffer of interleaved float samples.
    /// </summary>
    public event Action<float[]>? AudioDataAvailable;

    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Explicitly resolve the default render endpoint to avoid COM 0x80070490
        // when WasapiLoopbackCapture() tries to find it internally on some systems.
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _capture = new WasapiLoopbackCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop() => _capture?.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // WASAPI loopback delivers 32-bit IEEE float PCM
        int sampleCount = e.BytesRecorded / sizeof(float);
        float[] samples = new float[sampleCount];
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        AudioDataAvailable?.Invoke(samples);
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Console.Error.WriteLine($"[AudioCapture] Stopped with error: {e.Exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }
}
