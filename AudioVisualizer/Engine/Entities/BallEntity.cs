using System.Windows;
using AudioVisualizer.Engine.Components;
using AudioVisualizer.Engine.Configuration;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Ball entity driven by a <see cref="BallPreset"/>. Bounces around the viewport
/// under gravity, colliding with the live spectrum bars (and optionally peaks) as
/// a dynamic floor surface. The ball has no Reactivity component — audio causality
/// flows through physics: audio drives bar heights → bars push the ball.
/// </summary>
public sealed class BallEntity : SceneEntity
{
    /// <summary>Construct a ball entity from a preset, wired to bounce off the given bar (and optional peak) surfaces.</summary>
    public BallEntity(Point position, BarEntity bars, PeakEntity? peaks,
                      BallPreset preset, Vector? initialVelocity = null)
    {
        Position = position;
        Velocity = initialVelocity ?? new Vector(0, 0);

        var peakPhysics = peaks?.Physics as PhysicsComponent.Peak;
        var physics = new PhysicsComponent.Ball(preset, bars.Bars, peakPhysics);
        Physics   = physics;
        Rendering = new RenderingComponent.Ball(physics);
        Input     = new InputComponent.Drag(physics);
    }
}
