using System.Windows;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Beach-ball entity. Bounces around the viewport under gravity, with optional
/// audio-reactive bouncing on bass and horizontal sway on treble.
/// </summary>
public sealed class BallEntity : SceneEntity
{
    /// <summary>Construct a beach-ball entity with physics, rendering, and optional reactivity.</summary>
    public BallEntity(Point position, double radius = 40, Vector? initialVelocity = null, bool audioReactive = true)
    {
        Position = position;
        Velocity = initialVelocity ?? new Vector(0, 0);

        var physics = new PhysicsComponent.Ball(radius);
        Physics   = physics;
        Rendering = new RenderingComponent.Ball(physics);

        if (audioReactive)
            Reactivity = new ReactivityComponent.Ball();
    }
}
