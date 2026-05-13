using System.Windows;
using WavBall.Components;

namespace WavBall.Entities;

/// <summary>
/// Goal zone entity. A static glowing ring that the player must guide the ball into.
/// When the ball enters the goal, fires <see cref="World.Collision"/> to signal
/// a stage clear. Has no reactivity or input — purely visual + trigger.
/// </summary>
public sealed class Goal : World
{
    /// <summary>Goal radius in pixels.</summary>
    public double Radius { get; }

    /// <summary>
    /// Controls whether the goal is active (visible and triggerable).
    /// When false, the goal is hidden and cannot be scored.
    /// </summary>
    public bool Enabled
    {
        get => _goalPhysics.Enabled;
        set
        {
            _goalPhysics.Enabled = value;
            _goalRendering.Enabled = value;
        }
    }

    private readonly Physics.Goal _goalPhysics;
    private readonly Rendering.Goal _goalRendering;

    /// <summary>Construct a goal at the given position, watching the specified ball for overlap.</summary>
    public Goal(Point position, double radius, World ball)
    {
        Position = position;
        Radius = radius;
        _goalPhysics = new Physics.Goal(radius, ball);
        _goalRendering = new Rendering.Goal(radius) { BallRef = ball };
        Physics   = _goalPhysics;
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
