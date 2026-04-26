using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioVisualizer;

/// <summary>
/// Captures system audio output via WASAPI loopback.
/// Works with Spotify, browsers, or any audio playing through the default output device.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    #region Fields
    /// <summary>
    /// The WASAPI loopback capture instance. Created on Start() and disposed on Stop()/Dispose().
    /// </summary>
    private WasapiLoopbackCapture? _capture;

    /// <summary>
    /// Indicates whether the object has been disposed.
    /// </summary>
    private bool _disposed;
    #endregion

    #region Properties
    /// <summary>
    /// The audio format of the captured data. Available after Start() is called.
    /// </summary>
    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    #region Events
    /// <summary>
    /// Fired on the capture thread with each new buffer of interleaved float samples.
    /// </summary>
    public event Action<float[]>? AudioDataAvailable;
    #endregion
    #endregion

    #region Methods

    /// <summary>
    /// Starts capturing audio from the default output device. 
    /// Must be called before accessing WaveFormat or receiving AudioDataAvailable events.
    /// </summary>
    public void Start()
    {
        // Prevent starting if already disposed
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Explicitly resolve the default render endpoint to avoid COM 0x80070490
        // when WasapiLoopbackCapture() tries to find it internally on some systems.
        using var enumerator = new MMDeviceEnumerator();

        // Get the default audio output device (render) for multimedia role
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        // Create and start the WASAPI loopback capture
        _capture = new WasapiLoopbackCapture(device);     // Initialize capture with the specified device
        _capture.DataAvailable += OnDataAvailable;        // Subscribe to capture events    
        _capture.RecordingStopped += OnRecordingStopped;  // Subscribe to capture stopped events for error handling
        _capture.StartRecording();                        // Start capturing audio
    }

    /// <summary>
    /// Stops audio recording if a capture session is active.
    /// </summary>
    public void Stop() => _capture?.StopRecording();

    /// <summary>
    /// Handles audio data when it becomes available from the input device.
    /// </summary>
    /// <param name="sender">The source of the audio data event.</param>
    /// <param name="e">The event data containing the recorded audio buffer and metadata.</param>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // If no audio data was recorded,
        // skip processing to avoid unnecessary allocations and events.
        if (e.BytesRecorded == 0) return;

        // WASAPI loopback delivers 32-bit IEEE float PCM
        int sampleCount = e.BytesRecorded / sizeof(float);

        // Convert the byte buffer to a float array for easier processing by subscribers.
        float[] samples = new float[sampleCount];

        // Copy the raw byte data from the capture buffer into the float array.
        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

        // Invoke the AudioDataAvailable event with the new audio samples for subscribers to process.
        AudioDataAvailable?.Invoke(samples);
    }

    /// <summary>
    /// Handles the event when audio recording is stopped, either due to an error or normal stop.
    /// </summary>
    /// <param name="sender">The source of the recording stopped event.</param>
    /// <param name="e">The event data containing information about the stop event.</param>
    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // If an exception occurred during recording,
        // log the error message to the console.
        if (e.Exception is not null)
            Console.Error.WriteLine($"[AudioCapture] Stopped with error: {e.Exception.Message}");
    }

    /// <summary>
    /// Disposes of the audio capture resources. Stops recording if active and releases unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        // If already disposed, do nothing to prevent multiple disposals.
        if (_disposed) return;

        // otherwise, mark as disposed...
        _disposed = true;       
        
        // ...and clean up resources
        _capture?.StopRecording(); // Ensure recording is stopped before disposing
        _capture?.Dispose();       // Dispose the capture instance to release unmanaged resources
        _capture = null;           // Clear the reference to allow garbage collection
    }
    #endregion
}
