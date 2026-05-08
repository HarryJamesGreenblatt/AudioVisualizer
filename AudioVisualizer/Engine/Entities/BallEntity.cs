using System.Windows;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Beach-ball entity. Bounces around the viewport under gravity, colliding with the
/// live spectrum bars (and optionally peaks) as a dynamic floor surface.
/// The ball has no Reactivity component — audio causality flows through physics:
/// audio drives bar heights → bars push the ball.
/// </summary>
public sealed class BallEntity : SceneEntity
{
    /// <summary>Construct a beach-ball entity wired to bounce off the given bar (and optional peak) surfaces.</summary>
    public BallEntity(Point position, BarEntity bars, PeakEntity? peaks = null,
                      double radius = 40, Vector? initialVelocity = null)
    {
        Position = position;
        Velocity = initialVelocity ?? new Vector(0, 0);

        var peakPhysics = peaks?.Physics as PhysicsComponent.Peak;
        var physics = new PhysicsComponent.Ball(radius, bars.Bars, peakPhysics);
        Physics   = physics;
        Rendering = new RenderingComponent.Ball(physics);
    }
}
