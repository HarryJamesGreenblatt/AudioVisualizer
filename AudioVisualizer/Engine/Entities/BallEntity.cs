using System.Windows;
using AudioVisualizer.Engine.Components.Physics;
using AudioVisualizer.Engine.Components.Reactivity;
using AudioVisualizer.Engine.Components.Rendering;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Beach ball entity. Bounces around the viewport under gravity, with optional
/// audio-reactive bouncing on bass and horizontal sway on treble.
/// </summary>
public sealed class BallEntity : SceneEntity
{
    #region Constructor
    /// <summary>
    /// Construct a beach ball entity with physics and rendering, optionally audio-reactive.
    /// </summary>
    /// <param name="position">Initial position in viewport coordinates.</param>
    /// <param name="radius">Ball radius in pixels.</param>
    /// <param name="initialVelocity">Initial velocity vector (defaults to zero).</param>
    /// <param name="audioReactive">If true, bass triggers bounces and treble adds sway.</param>
    public BallEntity(Point position, double radius = 40, Vector? initialVelocity = null, bool audioReactive = true)
    {
        Position = position;
        Velocity = initialVelocity ?? new Vector(0, 0);

        var physics = new BallPhysics(radius);
        Physics = physics;
        Rendering = new BallRenderer(physics);

        if (audioReactive)
            Reactivity = new BallReactivity();
    }
    #endregion
}
