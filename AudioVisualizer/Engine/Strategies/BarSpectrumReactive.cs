using System;
using System.Windows;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// AudioReactive component for the spectrum bar entity.
/// Receives mel-band data and updates bar heights with global-max scaling.
/// Runs before physics so peak-hold has fresh bar heights to track.
/// </summary>
public sealed class BarSpectrumReactive : IAudioReactiveComponent
{
    #region Fields
    /// <summary>
    /// Running maximum across all bands for dynamic normalization.
    /// Rises instantly, decays slowly to prevent constant rescaling.
    /// </summary>
    private float _globalMax = 0.01f;
    #endregion

    #region Properties
    /// <summary>
    /// Scaled bar heights (pixels), updated each frame audio arrives.
    /// </summary>
    public float[] BarHeights { get; private set; } = [];

    /// <summary>
    /// Number of frequency bands being tracked.
    /// </summary>
    public int BandCount => BarHeights.Length;
    #endregion

    #region Methods
    /// <summary>
    /// Map incoming mel-band magnitudes into pixel-space bar heights using dynamic global-max scaling.
    /// </summary>
    /// <param name="entity">The owning entity (Position.Y encodes viewport height).</param>
    /// <param name="bands">Current mel-band magnitudes from the FFT processor.</param>
    public void React(SceneEntity entity, ReadOnlySpan<float> bands)
    {
        if (bands.IsEmpty) return;

        if (BarHeights.Length != bands.Length)
            BarHeights = new float[bands.Length];

        // Global max: instant rise, very slow decay (0.9995/frame @ 120fps physics ≈ same feel as 60fps render)
        float currentMax = 0.001f;
        foreach (float v in bands)
            if (v > currentMax) currentMax = v;

        if (currentMax > _globalMax)
            _globalMax = currentMax;
        else
            _globalMax = Math.Max(0.01f, _globalMax * 0.9995f);

        // Scale into pixel space using viewport height from entity position (Y stores viewport height)
        float viewportHeight = (float)entity.Position.Y;
        float scale = viewportHeight / _globalMax;

        for (int i = 0; i < bands.Length; i++)
            BarHeights[i] = bands[i] * scale;
    }
    #endregion
}
