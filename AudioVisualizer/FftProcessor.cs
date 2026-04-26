using NWaves.Transforms;
using NWaves.Windows;

namespace AudioVisualizer;

/// <summary>
/// Converts PCM float samples into a mel-scale frequency magnitude spectrum using NWaves.
/// Mel-scale maps FFT bins the way human hearing perceives frequency — more resolution
/// in bass/mids, less in high treble — giving a much more natural-looking visualizer.
/// </summary>
public sealed class FftProcessor
{
    private readonly int _fftSize;
    private readonly int _sampleRate;
    private readonly float[] _window;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly float[] _smoothed;
    private readonly RealFft _fft;

    // Mel-scale filterbank: each entry is (startBin, endBin) for one output band
    private readonly (int start, int end)[] _melBins;

    public int BandCount { get; }

    /// <param name="fftSize">Must be a power of 2. 2048 is a good default.</param>
    /// <param name="bandCount">Number of mel-spaced output bands.</param>
    /// <param name="sampleRate">Sample rate of the capture device (typically 44100 or 48000).</param>
    public FftProcessor(int fftSize = 2048, int bandCount = 64, int sampleRate = 44100)
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

        _melBins = BuildMelFilterbank(fftSize, sampleRate, bandCount, minFreq: 20f, maxFreq: 20000f);
    }

    /// <summary>
    /// Process a buffer of interleaved PCM samples. Writes mel-band magnitudes to <paramref name="bands"/>.
    /// </summary>
    public void Process(float[] samples, int channels, Span<float> bands)
    {
        if (bands.Length != BandCount)
            throw new ArgumentException($"bands must have length {BandCount}");

        // Mix down to mono, take the last _fftSize frames
        int monoCount = samples.Length / channels;
        int start = Math.Max(0, monoCount - _fftSize);
        int count = Math.Min(monoCount, _fftSize);

        Array.Clear(_re, 0, _fftSize);
        Array.Clear(_im, 0, _fftSize);

        float inputPeak = 0f;
        for (int i = 0; i < count; i++)
        {
            float mono = 0f;
            int srcBase = (start + i) * channels;
            for (int ch = 0; ch < channels; ch++)
                mono += samples[srcBase + ch];
            mono /= channels;
            float abs = MathF.Abs(mono);
            if (abs > inputPeak) inputPeak = abs;
            _re[i] = mono * _window[i];
        }

        // If the frame is below the noise floor, treat as silence:
        // skip the FFT entirely and decay bands directly — avoids the WASAPI
        // "silent buffer" problem where non-zero noise keeps resetting the decay.
        const float SilenceThreshold = 5e-6f;
        if (inputPeak < SilenceThreshold)
        {
            for (int b = 0; b < BandCount; b++)
            {
                _smoothed[b] *= 0.7f;
                bands[b] = _smoothed[b];
            }
            return;
        }

        _fft.Direct(_re, _re, _im);

        // Map FFT bins to mel bands with peak-picking
        for (int b = 0; b < BandCount; b++)
        {
            var (binStart, binEnd) = _melBins[b];
            float peak = 0f;
            for (int bin = binStart; bin < binEnd; bin++)
            {
                float mag = MathF.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]);
                if (mag > peak) peak = mag;
            }

            // Asymmetric smoothing: fast attack, fast release
            float alpha = peak > _smoothed[b] ? 0.4f : 0.6f;
            _smoothed[b] = (1f - alpha) * _smoothed[b] + alpha * peak;
            bands[b] = _smoothed[b];
        }
    }

    /// <summary>
    /// Build a mel-scale filterbank mapping FFT bins to <paramref name="bandCount"/> bands.
    /// Mel scale: freq -> 2595 * log10(1 + freq/700). Perceptually uniform spacing.
    /// </summary>
    private static (int start, int end)[] BuildMelFilterbank(
        int fftSize, int sampleRate, int bandCount, float minFreq, float maxFreq)
    {
        static float HzToMel(float hz) => 2595f * MathF.Log10(1f + hz / 700f);
        static float MelToHz(float mel) => 700f * (MathF.Pow(10f, mel / 2595f) - 1f);

        float melMin = HzToMel(minFreq);
        float melMax = HzToMel(maxFreq);
        float melStep = (melMax - melMin) / (bandCount + 1);

        // Mel-spaced center frequencies
        float[] centerFreqs = new float[bandCount + 2];
        for (int i = 0; i < centerFreqs.Length; i++)
            centerFreqs[i] = MelToHz(melMin + i * melStep);

        // Convert center frequencies to FFT bin indices
        float binWidth = sampleRate / (float)fftSize;
        int nyquist = fftSize / 2;

        int FreqToBin(float hz) => (int)Math.Clamp(hz / binWidth, 0, nyquist - 1);

        var filters = new (int start, int end)[bandCount];
        for (int b = 0; b < bandCount; b++)
        {
            int s = FreqToBin(centerFreqs[b]);
            int e = FreqToBin(centerFreqs[b + 2]);
            filters[b] = (s, Math.Max(s + 1, e));
        }

        return filters;
    }
}
