using System.Windows;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Goal zone entity. A static glowing ring that the player must guide the ball into.
/// When the ball enters the goal, fires <see cref="SceneEntity.Collision"/> to signal
/// a stage clear. Has no reactivity or input — purely visual + trigger.
/// </summary>
public sealed class GoalEntity : SceneEntity
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

    private readonly PhysicsComponent.Goal _goalPhysics;
    private readonly RenderingComponent.Goal _goalRendering;

    /// <summary>Construct a goal at the given position, watching the specified ball for overlap.</summary>
    public GoalEntity(Point position, double radius, SceneEntity ball)
    {
        Position = position;
        Radius = radius;
        _goalPhysics = new PhysicsComponent.Goal(radius, ball);
        _goalRendering = new RenderingComponent.Goal(radius);
        Physics   = _goalPhysics;
        Rendering = _goalRendering;
    }
}
