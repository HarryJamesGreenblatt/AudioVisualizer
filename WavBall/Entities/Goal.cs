using System.Windows;
using WavBall.Components;

namespace WavBall.Entities;

/// <summary>
/// Goal zone entity. A glowing ring the player must guide the ball into. When the ball
/// enters, fires <see cref="World.Collision"/> to signal a stage clear. The ring is also
/// an autonomous "moth-to-flame" agent that drifts toward whichever frequency band is
/// most musically active — the motion comes from <see cref="Steering.Goal"/>, the trigger
/// detection from <see cref="Physics.Goal"/>.
/// </summary>
public sealed class Goal : World
{
    /// <summary>Goal radius in pixels.</summary>
    public double Radius { get; }

    /// <summary>
    /// Controls whether the goal is active (visible, triggerable, and steering). When false
    /// the goal is hidden, cannot be scored, and freezes in place — used by anti-cheat to
    /// suspend the goal while the user drags the ball.
    /// </summary>
    public bool Enabled
    {
        get => _goalPhysics.Enabled;
        set
        {
            _goalPhysics.Enabled = value;
            _goalRendering.Enabled = value;
            _goalSteering.Enabled = value;
            _goalCharge.Enabled = value;
        }
    }

    private readonly Physics.Goal _goalPhysics;
    private readonly Steering.Goal _goalSteering;
    private readonly Rendering.Goal _goalRendering;
    private readonly Charge.Goal _goalCharge;

    /// <summary>
    /// Minimum <see cref="Steering.Goal.Satiety"/> required for the goal to be
    /// collidable. A freshly-spawned goal sits at satiety 0 and is intentionally
    /// pass-through for the first ~1 s of feeding; once it crosses this line it
    /// stays collidable through the entire Sated traverse — even when crossing
    /// cold mid-air zones where instantaneous charge is near zero — until satiety
    /// drains back below the threshold at the destination.
    /// </summary>
    private const double ArmSatietyThreshold = 0.20;

    /// <summary>
    /// Construct a goal at the given position, watching the specified ball for overlap and
    /// using the spectrum (<paramref name="bars"/>) to drive both autonomous motion and the
    /// reactive glow.
    /// </summary>
    public Goal(Point position, double radius, World ball, Reactivity.Bar bars)
    {
        // Hover offset shared between the steering target and the charge field's
        // feeding-zone centers — the goal hovers this many pixels above each bar's
        // top, and the Gaussian sensor centers its zones at the same point.
        const double hoverOffset = 35.0;

        Position = position;
        Radius = radius;
        _goalPhysics   = new Physics.Goal(radius, ball);
        _goalSteering  = new Steering.Goal(radius, position, bars);
        _goalRendering = new Rendering.Goal(radius, bars) { BallRef = ball };
        _goalCharge    = new Charge.Goal(bars, hoverOffset);

        // Wire the appetite signals between components. Composition lives here in
        // the entity; components stay decoupled.
        //
        //   Steering    ← Charge.Value      (sensor feeds satiety integrator)
        //   Physics     ← Steering.Satiety  (slow integrated fullness gates collision)
        //   Rendering   ← Charge.Value      AND  Steering.Satiety  (two-layer visual)
        //
        // The renderer takes both signals: the inner ring + crosshair (reticle) tracks
        // charge so the player sees instantaneous "I can score now" intensity, while the
        // outer glow halo tracks satiety so the player sees the slow appetite-cycle
        // state independently of food contact. Physics arming follows satiety so a
        // Sated goal stays collidable across cold mid-air transits — the reticle's
        // dimmer-but-not-dark appearance during that traverse is the *correct* signal:
        // "I'm full, between bites, but still scoreable."
        _goalSteering.ChargeSource    = () => _goalCharge.Value;
        _goalPhysics.IsLive           = () => _goalSteering.Satiety > ArmSatietyThreshold;
        _goalRendering.ChargeSource   = () => _goalCharge.Value;
        _goalRendering.SatietySource  = () => _goalSteering.Satiety;

        Physics   = _goalPhysics;
        Steering  = _goalSteering;
        Rendering = _goalRendering;
        Charge    = _goalCharge;
    }

    /// <summary>
    /// Update the ball reference on the goal's renderer (e.g. after ball respawn).
    /// </summary>
    public void SetBallRef(World? ball)
    {
        _goalRendering.BallRef = ball;
    }
}
