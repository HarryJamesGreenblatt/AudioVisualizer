using System.Windows;
using AudioVisualizer.Components;
using AudioVisualizer.Configuration;

namespace AudioVisualizer.Entities;

/// <summary>
/// Ball entity driven by a <see cref="BallPreset"/>. Bounces around the viewport
/// under gravity, colliding with the live spectrum bars (and optionally peaks) as
/// a dynamic floor surface. The ball has no Reactivity component — audio causality
/// flows through physics: audio drives bar heights → bars push the ball.
/// </summary>
public sealed class Ball : World
{
    /// <summary>Construct a ball entity from a preset, wired to bounce off the given bar (and optional peak) surfaces.</summary>
    public Ball(Point position, Bar bars, Peak? peaks,
                      BallPreset preset, Vector? initialVelocity = null)
    {
        Position = position;
        Velocity = initialVelocity ?? new Vector(0, 0);

        var peakPhysics = peaks?.Physics as Physics.Peak;
        var physics = new Physics.Ball(preset, bars.Bars, peakPhysics);
        Physics   = physics;
        Rendering = new Rendering.Ball(physics);
        Input = new Components.Input.Drag(physics);
    }
}
