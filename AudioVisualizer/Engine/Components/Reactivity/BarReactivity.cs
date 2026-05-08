using System;
using System.Windows;

namespace AudioVisualizer.Engine.Components.Reactivity;

/// <summary>
/// Reactivity component for the spectrum bar entity.
/// Receives mel-band data and updates bar heights with global-max scaling.
/// Runs before physics so peak-hold has fresh bar heights to track.
/// </summary>
public sealed class BarReactivity : IReactivityComponent
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
    /// <inheritdoc />
    public void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport)
    {
        if (bands.IsEmpty) return;

        if (BarHeights.Length != bands.Length)
            BarHeights = new float[bands.Length];

        // Global max: instant rise, very slow decay
        float currentMax = 0.001f;
        foreach (float v in bands)
            if (v > currentMax) currentMax = v;

        if (currentMax > _globalMax)
            _globalMax = currentMax;
        else
            _globalMax = Math.Max(0.01f, _globalMax * 0.9995f);

        // Scale into pixel space using viewport height
        float scale = (float)viewport.Height / _globalMax;

        for (int i = 0; i < bands.Length; i++)
            BarHeights[i] = bands[i] * scale;
    }
    #endregion
}
