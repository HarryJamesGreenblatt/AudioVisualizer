using NWaves.Transforms;
using NWaves.Windows;

namespace AudioVisualizer.Services;

/// <summary>
/// Converts PCM float samples into a mel-scale frequency magnitude spectrum using NWaves.
/// Mel-scale maps FFT bins the way human hearing perceives frequency — more resolution
/// in bass/mids, less in high treble — giving a much more natural-looking visualizer.
/// </summary>
public sealed class FftProcessingService
{
    #region Fields
    /// <summary>
    /// Number of samples per FFT frame. Must be a power of 2.
    /// Larger values give better frequency resolution but higher latency.
    /// </summary>
    private readonly int _fftSize;

    /// <summary>
    /// Sample rate of the capture device, in Hz (typically 44100 or 48000).
    /// Used to convert FFT bin indices to real frequencies.
    /// </summary>
    private readonly int _sampleRate;

    /// <summary>
    /// Precomputed Blackman window coefficients applied to each frame before the FFT.
    /// Reduces spectral leakage caused by the implicit rectangular window of a finite buffer.
    /// </summary>
    private readonly float[] _window;

    /// <summary>
    /// Real part of the FFT input/output buffer. Reused each frame to avoid allocations.
    /// </summary>
    private readonly float[] _re;

    /// <summary>
    /// Imaginary part of the FFT output buffer. Reused each frame to avoid allocations.
    /// </summary>
    private readonly float[] _im;

    /// <summary>
    /// Per-band magnitude values carried over from the previous frame.
    /// Used by the asymmetric smoothing filter to produce a natural attack/release envelope.
    /// </summary>
    private readonly float[] _smoothed;

    /// <summary>
    /// The NWaves real-valued FFT engine. Operates in-place on <see cref="_re"/> and <see cref="_im"/>.
    /// </summary>
    private readonly RealFft _fft;

    /// <summary>
    /// Mel-scale filterbank: each entry is (startBin, endBin) for one output band.
    /// Built once in the constructor from the FFT size and sample rate.
    /// </summary>
    private readonly (int start, int end)[] _melBins;

    /// <summary>
    /// Precomputed per-band gain multipliers that progressively boost higher frequency bands.
    /// Compensates for the natural spectral roll-off in music (~3-6 dB/octave) so that
    /// high-frequency content like snares and hi-hats registers visually.
    /// </summary>
    private readonly float[] _bandGain;
    #endregion

    #region Properties
    /// <summary>
    /// Number of mel-spaced output bands produced by <see cref="Process"/>.
    /// </summary>
    public int BandCount { get; }
    #endregion

    #region Methods
    /// <summary>
    /// Initializes the FFT processor and precomputes the mel filterbank.
    /// </summary>
    /// <param name="fftSize">Must be a power of 2. 2048 is a good default.</param>
    /// <param name="bandCount">Number of mel-spaced output bands.</param>
    /// <param name="sampleRate">Sample rate of the capture device (typically 44100 or 48000).</param>
    public FftProcessingService(int fftSize = 2048, int bandCount = 64, int sampleRate = 44100)
    {
        _fftSize    = fftSize;
        _sampleRate = sampleRate;
        BandCount   = bandCount;
        _smoothed   = new float[bandCount];
        _re         = new float[fftSize];
        _im         = new float[fftSize];
        _fft        = new RealFft(fftSize);

        // Blackman window: good side-lobe rejection, less spectral leakage
        // than Hanning — better for music with many simultaneous frequencies
        _window = Window.OfType(WindowType.Blackman, fftSize);

        // Build the mel filterbank once; it only depends on static parameters
        _melBins = BuildMelFilterbank(fftSize, sampleRate, bandCount, minFreq: 20f, maxFreq: 20000f);

        // Precompute a mild frequency-dependent gain curve: higher bands get a modest boost.
        // Compensates for the natural spectral roll-off in music without overdriving.
        _bandGain = new float[bandCount];
        for (int b = 0; b < bandCount; b++)
        {
            float t = (float)b / (bandCount - 1);           // 0 at bass, 1 at treble
            _bandGain[b] = 1f + 2f * t * t;                 // quadratic ramp: 1× → 3×
        }
    }

    /// <summary>
    /// Processes a buffer of interleaved PCM samples and writes mel-band magnitudes
    /// to <paramref name="bands"/>. Called on the WASAPI capture thread each frame.
    /// </summary>
    /// <param name="samples">Interleaved float PCM samples from the capture device.</param>
    /// <param name="channels">Number of audio channels (e.g., 2 for stereo).</param>
    /// <param name="bands">
    /// Output span of length <see cref="BandCount"/> that receives the smoothed
    /// mel-band magnitudes for this frame.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="bands"/> does not have exactly <see cref="BandCount"/> elements.
    /// </exception>
    public void Process(float[] samples, int channels, Span<float> bands)
    {
        if (bands.Length != BandCount)
            throw new ArgumentException($"bands must have length {BandCount}");

        // Mix down to mono and take the most recent _fftSize frames.
        // Discarding older samples keeps latency low without affecting frequency resolution.
        int monoCount = samples.Length / channels;
        int start = Math.Max(0, monoCount - _fftSize);
        int count = Math.Min(monoCount, _fftSize);

        // Clear both buffers so leftover data from the previous frame does not corrupt the FFT
        Array.Clear(_re, 0, _fftSize);
        Array.Clear(_im, 0, _fftSize);

        float inputPeak = 0f;
        for (int i = 0; i < count; i++)
        {
            // Sum all channels and divide to produce a mono sample
            float mono = 0f;
            int srcBase = (start + i) * channels;
            for (int ch = 0; ch < channels; ch++)
                mono += samples[srcBase + ch];
            mono /= channels;

            // Track the loudest sample in this frame to detect silence
            float abs = MathF.Abs(mono);
            if (abs > inputPeak) inputPeak = abs;

            // Apply the window function before writing to the real FFT input buffer
            _re[i] = mono * _window[i];
        }

        // If the frame is below the noise floor, treat as silence:
        // Skip the FFT entirely when the input is below the noise floor.
        // WASAPI loopback delivers non-zero samples even when nothing is playing;
        // a threshold of 1e-4 reliably catches this while passing any real audio.
        const float SilenceThreshold = 1e-4f;
        if (inputPeak < SilenceThreshold)
        {
            for (int b = 0; b < BandCount; b++)
            {
                // Aggressive decay toward zero — reaches imperceptible levels within ~0.5s
                _smoothed[b] *= 0.85f;
                if (_smoothed[b] < 1e-5f) _smoothed[b] = 0f;  // snap to zero to avoid perpetual residue
                bands[b] = _smoothed[b];
            }
            return;
        }

        // Run the in-place real FFT; results are written back into _re and _im
        _fft.Direct(_re, _re, _im);

        // Map FFT bins to mel bands using peak-picking within each band's bin range
        for (int b = 0; b < BandCount; b++)
        {
            var (binStart, binEnd) = _melBins[b];
            float peak = 0f;
            for (int bin = binStart; bin < binEnd; bin++)
            {
                // Compute the magnitude of the complex FFT output for this bin
                float mag = MathF.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]);
                if (mag > peak) peak = mag;
            }

            // Apply per-band gain to compensate for natural spectral roll-off
            peak *= _bandGain[b];

            // Square-root compression: gently lifts quieter values so that
            // high-frequency transients (snares, hi-hats) become visible
            // without overdriving everything into the top of the range.
            peak = MathF.Sqrt(peak);

            // Asymmetric smoothing: near-instant attack for tight transient response,
            // moderate release to avoid jitter on decay. This eliminates the perceived
            // latency that a symmetric 0.6 blend introduces on rising edges.
            float prev = _smoothed[b];
            float alpha = peak >= prev ? 0.9f : 0.55f;  // attack: 90% new, release: 55% new
            _smoothed[b] = (1f - alpha) * prev + alpha * peak;
            bands[b] = _smoothed[b];
        }
    }

    /// <summary>
    /// Builds a mel-scale filterbank that maps FFT bins to <paramref name="bandCount"/> perceptual bands.
    /// </summary>
    /// <remarks>
    /// Mel scale: <c>mel = 2595 * log10(1 + hz / 700)</c>. Spacing bands uniformly on the mel scale
    /// mimics the non-linear frequency resolution of human hearing — denser in the lows, sparser in
    /// the highs — which makes the visualizer look natural across all musical content.
    /// </remarks>
    /// <param name="fftSize">Number of FFT samples (determines bin width).</param>
    /// <param name="sampleRate">Capture device sample rate in Hz.</param>
    /// <param name="bandCount">Number of output mel bands.</param>
    /// <param name="minFreq">Lowest frequency to include in the filterbank, in Hz.</param>
    /// <param name="maxFreq">Highest frequency to include in the filterbank, in Hz.</param>
    /// <returns>
    /// An array of <c>(startBin, endBin)</c> pairs, one per mel band.
    /// Each pair defines the inclusive FFT bin range to peak-pick for that band.
    /// </returns>
    private static (int start, int end)[] BuildMelFilterbank(
        int fftSize, int sampleRate, int bandCount, float minFreq, float maxFreq)
    {
        static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
        static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);

        float melMin  = HzToMel(minFreq);
        float melMax  = HzToMel(maxFreq);
        float melStep = (melMax - melMin) / (bandCount + 1);

        // Compute bandCount + 2 mel-spaced center frequencies so that each band has
        // a left and right edge (the extra two points form the outer boundaries)
        float[] centerFreqs = new float[bandCount + 2];
        for (int i = 0; i < centerFreqs.Length; i++)
            centerFreqs[i] = MelToHz(melMin + i * melStep);

        // Convert center frequencies to FFT bin indices
        float binWidth = sampleRate / (float)fftSize;
        int nyquist = fftSize / 2;

        // Clamp to valid bin range to avoid out-of-bounds access on the FFT output
        int FreqToBin(float hz) => (int)Math.Clamp(hz / binWidth, 0, nyquist - 1);

        var filters = new (int start, int end)[bandCount];
        for (int b = 0; b < bandCount; b++)
        {
            int s = FreqToBin(centerFreqs[b]);
            int e = FreqToBin(centerFreqs[b + 2]);

            // Guarantee at least one bin per band, even for very narrow high-frequency bands
            filters[b] = (s, Math.Max(s + 1, e));
        }

        return filters;
    }
    #endregion
}
