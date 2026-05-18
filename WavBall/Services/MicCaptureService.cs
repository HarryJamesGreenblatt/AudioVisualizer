using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WavBall.Services;

/// <summary>
/// Captures audio from the default input device (microphone) via WASAPI shared-mode capture.
/// Mirrors <see cref="AudioCaptureService"/> so the same FFT pipeline can be applied.
///
/// Mic and loopback streams are kept separate — mixing happens at the band level
/// in MainWindow (RMS combine) to avoid sample-rate / phase complications.
/// </summary>
public sealed class MicCaptureService : IDisposable
{
    private WasapiCapture? _capture;
    private bool _disposed;

    /// <summary>The device's native capture format. Available after <see cref="Start"/>.</summary>
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    /// <summary>Fired on the capture thread with each new buffer of interleaved float samples.</summary>
    public event Action<float[]>? AudioDataAvailable;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capture != null) return; // already started

        using var enumerator = new MMDeviceEnumerator();
        // IMPORTANT: use Role.Multimedia, not Role.Communications.
        // Communications role tells Windows "this is a phone/meeting app", which
        // triggers system-wide comms-mode processing (narrowband filtering, ducking)
        // on *all* playback — Spotify, browsers, everything goes tinny.
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

        _capture = new WasapiCapture(device);
        _capture.DataAvailable    += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c == null) return;
        c.DataAvailable    -= OnDataAvailable;
        c.RecordingStopped -= OnRecordingStopped;
        try { c.StopRecording(); } catch { /* device may have vanished */ }
        c.Dispose();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        var fmt = _capture?.WaveFormat;
        if (fmt == null) return;

        // WasapiCapture can deliver float32, int16, or int24 depending on device defaults.
        // Always normalize to float[] in [-1, 1] so downstream FFT code is format-agnostic.
        float[] samples;
        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            int count = e.BytesRecorded / sizeof(float);
            samples = new float[count];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
        }
        else if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
        {
            int count = e.BytesRecorded / sizeof(short);
            samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
        }
        else
        {
            return; // unsupported format — silently drop rather than crash
        }

        AudioDataAvailable?.Invoke(samples);
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Console.Error.WriteLine($"[MicCapture] Stopped with error: {e.Exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
