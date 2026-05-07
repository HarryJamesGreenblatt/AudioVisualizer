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
    /// Hold timer per band: counts down physics ticks before the peak begins descending.
    /// </summary>
    private int[] _holdTimer = [];

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
    /// Advance peak indicators one physics tick. Each peak snaps to the bar if
    /// the bar rises above it, holds briefly, then falls under gentle gravity.
    /// Gravity is tuned so that within a typical viewport fall (~700px), peak velocity
    /// never exceeds ~3px per render frame — fast enough to feel physical, slow enough
    /// to avoid the "multiple peaks" ghosting artifact.
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
            _holdTimer = new int[barHeights.Length];
        }

        // Hold duration before gravity kicks in (~250ms at 120Hz).
        // Gives the eye time to register the peak position before it starts falling.
        const int holdTicks = 30;

        // Gentle gravity: 0.06 px/tick². After a full 700px fall the peak reaches
        // ~9px/tick ≈ 4.5px per 60Hz render frame — perceptible motion without ghosting.
        // Compare to original 0.25f which hit 10+ px/render-frame quickly.
        const float gravity = 0.06f;

        for (int i = 0; i < barHeights.Length; i++)
        {
            if (barHeights[i] >= _peakHold[i])
            {
                // Bar rose above peak → snap peak to bar, reset fall state
                _peakHold[i] = barHeights[i];
                _peakVelocity[i] = 0f;
                _holdTimer[i] = holdTicks;
            }
            else if (_holdTimer[i] > 0)
            {
                // Holding at peak — count down before descent begins
                _holdTimer[i]--;
            }
            else
            {
                // Gravity-driven descent: natural 1D kinematics
                _peakVelocity[i] += gravity;
                _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
            }
        }
    }
    #endregion
}
