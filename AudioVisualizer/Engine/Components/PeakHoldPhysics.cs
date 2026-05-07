using System;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Physics component for peak-hold indicators.
/// Runs every physics tick (independent of audio data arrival), so peaks fall
/// smoothly with gravity even when audio stops — fixing the frozen-peaks bug.
/// </summary>
public sealed class PeakHoldPhysics : IPhysicsSystem
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

    /// <inheritdoc />
    public float Gravity => 0.06f;

    /// <inheritdoc />
    public float Restitution => 0.3f;
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
    /// Ensure internal arrays match the current band count.
    /// Called at the start of each pipeline phase that reads bar data.
    /// </summary>
    private bool EnsureBuffers()
    {
        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return false;

        if (_peakHold.Length != barHeights.Length)
        {
            _peakHold = new float[barHeights.Length];
            _peakVelocity = new float[barHeights.Length];
            _holdTimer = new int[barHeights.Length];
        }
        return true;
    }

    /// <inheritdoc />
    public void ApplyForces(float dt)
    {
        if (!EnsureBuffers()) return;

        for (int i = 0; i < _peakHold.Length; i++)
        {
            if (_holdTimer[i] > 0)
            {
                // Holding at peak — count down before descent begins
                _holdTimer[i]--;
            }
            else
            {
                // Gravity accumulates into velocity (positive = downward)
                _peakVelocity[i] += Gravity;
            }
        }
    }

    /// <inheritdoc />
    public void Integrate(float dt)
    {
        if (_peakHold.Length == 0) return;

        for (int i = 0; i < _peakHold.Length; i++)
        {
            if (_holdTimer[i] > 0) continue; // held peaks don't move

            // Velocity → position (negative velocity = upward bounce)
            _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
        }
    }

    /// <inheritdoc />
    public void ResolveCollisions(float dt)
    {
        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return;

        // Hold duration before gravity kicks in (~250ms at 120Hz).
        const int holdTicks = 30;

        // Minimum impact velocity to trigger a bounce. Below this threshold the
        // peak settles onto the bar instead of micro-bouncing indefinitely.
        const float bounceThreshold = 0.5f;

        for (int i = 0; i < barHeights.Length; i++)
        {
            if (barHeights[i] >= _peakHold[i])
            {
                if (_peakVelocity[i] > bounceThreshold)
                {
                    // Peak was falling and impacted the bar → elastic bounce.
                    _peakHold[i] = barHeights[i];
                    _peakVelocity[i] = -(  _peakVelocity[i] * Restitution);
                    _holdTimer[i] = 0; // no hold during active bounce
                }
                else
                {
                    // Bar rose into peak, or bounce has decayed → settle cleanly.
                    _peakHold[i] = barHeights[i];
                    _peakVelocity[i] = 0f;
                    _holdTimer[i] = holdTicks;
                }
            }
        }
    }
    #endregion
}
