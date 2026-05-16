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
        }
    }

    private readonly Physics.Goal _goalPhysics;
    private readonly Steering.Goal _goalSteering;
    private readonly Rendering.Goal _goalRendering;

    /// <summary>
    /// Construct a goal at the given position, watching the specified ball for overlap and
    /// using the spectrum (<paramref name="bars"/>) to drive both autonomous motion and the
    /// reactive glow.
    /// </summary>
    public Goal(Point position, double radius, World ball, Reactivity.Bar bars)
    {
        Position = position;
        Radius = radius;
        _goalPhysics   = new Physics.Goal(radius, ball);
        _goalSteering  = new Steering.Goal(radius, position, bars);
        _goalRendering = new Rendering.Goal(radius, bars) { BallRef = ball };

        // Wire the steering's charge state into the other two components so the goal
        // can only score once it's musically engaged AND visually shows that state.
        // Composition lives here in the entity; components stay decoupled.
        _goalPhysics.IsLive = () => _goalSteering.IsArmed;
        _goalRendering.ChargeSource = () => _goalSteering.Charge;

        Physics   = _goalPhysics;
        Steering  = _goalSteering;
        Rendering = _goalRendering;
    }

    /// <summary>
    /// Update the ball reference on the goal's renderer (e.g. after ball respawn).
    /// </summary>
    public void SetBallRef(World? ball)
    {
        _goalRendering.BallRef = ball;
    }
}
