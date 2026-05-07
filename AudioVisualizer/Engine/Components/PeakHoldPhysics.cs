using System;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Physics component for peak-hold indicators.
/// Runs every physics tick (independent of audio data arrival), so peaks fall
/// smoothly with gravity even when audio stops — fixing the frozen-peaks bug.
/// </summary>
public sealed class PeakHoldPhysics : IPhysicsComponent
{
    #region Fields
    /// <summary>
    /// Current peak hold heights in pixels, one per frequency band.
    /// </summary>
    private float[] _peakHold = [];

    /// <summary>
    /// Downward velocity of each peak indicator (accumulated gravity).
    /// </summary>
    private float[] _peakVelocity = [];

    /// <summary>
    /// Reference to the audio-reactive component that provides bar heights to track against.
    /// </summary>
    private readonly BarSpectrumReactive _bars;
    #endregion

    #region Properties
    /// <summary>
    /// Peak hold heights in pixels, readable by the render component.
    /// </summary>
    public float[] PeakHeights => _peakHold;
    #endregion

    #region Constructor
    /// <summary>
    /// Create a peak-hold physics component coupled to the given bar data source.
    /// </summary>
    /// <param name="bars">The audio-reactive component providing current bar heights.</param>
    public PeakHoldPhysics(BarSpectrumReactive bars)
    {
        _bars = bars;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Advance peak indicators one physics tick. Each peak either snaps to the bar
    /// (if the bar rose above it) or falls under gravity.
    /// </summary>
    /// <param name="entity">The owning entity (unused for peak logic).</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    public void Update(SceneEntity entity, float dt)
    {
        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return;

        if (_peakHold.Length != barHeights.Length)
        {
            _peakHold = new float[barHeights.Length];
            _peakVelocity = new float[barHeights.Length];
        }

        // Gravity constant tuned for 120Hz fixed timestep
        // Original was 0.5f/frame at ~60fps → equivalent is ~0.25f/tick at 120fps
        const float gravity = 0.25f;

        for (int i = 0; i < barHeights.Length; i++)
        {
            if (barHeights[i] >= _peakHold[i])
            {
                // Bar rose above peak → snap peak to bar, reset velocity
                _peakHold[i] = barHeights[i];
                _peakVelocity[i] = 0f;
            }
            else
            {
                // Peak falls under gravity (independent of audio data arrival)
                _peakVelocity[i] += gravity;
                _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
            }
        }
    }
    #endregion
}
