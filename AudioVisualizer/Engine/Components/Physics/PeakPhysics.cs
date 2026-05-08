using System;
using System.Windows;
using AudioVisualizer.Engine.Components.Reactivity;

namespace AudioVisualizer.Engine.Components.Physics;

/// <summary>
/// Physics component for peak-hold indicators.
/// Each peak has gravity-driven descent, hold timer, and elastic bounce on contact
/// with the underlying bar height. Reads bar heights from a sibling BarEntity's
/// reactivity component (Component pattern: cross-entity coupling via reference).
/// </summary>
public sealed class PeakPhysics : IPhysicsComponent
{
    #region Fields
    /// <summary>
    /// Reference to the bar entity's reactivity component for current bar heights.
    /// </summary>
    private readonly BarReactivity _bars;

    /// <summary>
    /// Current peak hold heights in pixels, one per frequency band.
    /// </summary>
    private float[] _peakHold = [];

    /// <summary>
    /// Downward velocity of each peak indicator (accumulated gravity).
    /// </summary>
    private float[] _peakVelocity = [];

    /// <summary>
    /// Hold timer per band: counts down ticks before the peak begins descending.
    /// </summary>
    private int[] _holdTimer = [];

    /// <summary>
    /// Hold duration before gravity kicks in (~250ms at 120Hz).
    /// </summary>
    private const int HoldTicks = 30;

    /// <summary>
    /// Minimum impact velocity to trigger a bounce. Below this the peak settles.
    /// </summary>
    private const float BounceThreshold = 0.5f;
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
    /// Create a peak-hold physics component coupled to the given bar reactivity.
    /// </summary>
    /// <param name="bars">The bar entity's reactivity component, queried for live bar heights.</param>
    public PeakPhysics(BarReactivity bars)
    {
        _bars = bars;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Ensure internal arrays match the current band count.
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
    public void ApplyForces(SceneEntity entity, float dt)
    {
        if (!EnsureBuffers()) return;

        for (int i = 0; i < _peakHold.Length; i++)
        {
            if (_holdTimer[i] > 0)
                _holdTimer[i]--;
            else
                _peakVelocity[i] += Gravity;
        }
    }

    /// <inheritdoc />
    public void Integrate(SceneEntity entity, float dt)
    {
        if (_peakHold.Length == 0) return;

        for (int i = 0; i < _peakHold.Length; i++)
        {
            if (_holdTimer[i] > 0) continue;
            _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
        }
    }

    /// <inheritdoc />
    public void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
    {
        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return;

        for (int i = 0; i < barHeights.Length; i++)
        {
            if (barHeights[i] >= _peakHold[i])
            {
                if (_peakVelocity[i] > BounceThreshold)
                {
                    // Peak was falling and impacted the bar → elastic bounce
                    _peakHold[i] = barHeights[i];
                    _peakVelocity[i] = -(_peakVelocity[i] * Restitution);
                    _holdTimer[i] = 0;
                }
                else
                {
                    // Bar rose into peak, or bounce decayed → settle and hold
                    _peakHold[i] = barHeights[i];
                    _peakVelocity[i] = 0f;
                    _holdTimer[i] = HoldTicks;
                }
            }
        }
    }
    #endregion
}
