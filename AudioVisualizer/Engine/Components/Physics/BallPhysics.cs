using System;
using System.Windows;

namespace AudioVisualizer.Engine.Components.Physics;

/// <summary>
/// Physics component for beach ball entities.
/// Applies gravity and air drag, integrates velocity → position, bounces off viewport walls.
/// Each ball owns its own physics instance (Component pattern: physics is a property of the entity).
/// </summary>
public sealed class BallPhysics : IPhysicsComponent
{
    #region Properties
    /// <summary>
    /// Radius of the ball in pixels for collision detection.
    /// </summary>
    public double Radius { get; }

    /// <inheritdoc />
    public float Gravity => 800f;

    /// <inheritdoc />
    public float Restitution => 0.7f;
    #endregion

    #region Constructor
    /// <summary>
    /// Create a beach ball physics component with the given radius.
    /// </summary>
    /// <param name="radius">Ball radius in pixels.</param>
    public BallPhysics(double radius = 40)
    {
        Radius = radius;
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void ApplyForces(SceneEntity entity, float dt)
    {
        var vel = entity.Velocity;

        // Gravity
        vel.Y += Gravity * dt;

        // Gentle air resistance (1% drag per 60fps frame)
        vel *= Math.Pow(0.99, dt * 60);

        entity.Velocity = vel;
    }

    /// <inheritdoc />
    public void Integrate(SceneEntity entity, float dt)
    {
        entity.Position += entity.Velocity * dt;
    }

    /// <inheritdoc />
    public void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return;

        var pos = entity.Position;
        var vel = entity.Velocity;

        // Left wall
        if (pos.X - Radius < 0)
        {
            pos.X = Radius;
            vel.X = Math.Abs(vel.X) * Restitution;
        }
        // Right wall
        else if (pos.X + Radius > viewport.Width)
        {
            pos.X = viewport.Width - Radius;
            vel.X = -Math.Abs(vel.X) * Restitution;
        }

        // Floor
        if (pos.Y + Radius > viewport.Height)
        {
            pos.Y = viewport.Height - Radius;
            vel.Y = -Math.Abs(vel.Y) * Restitution;

            // Kill tiny bounces to prevent jitter
            if (Math.Abs(vel.Y) < 50)
                vel.Y = 0;
        }
        // Ceiling
        else if (pos.Y - Radius < 0)
        {
            pos.Y = Radius;
            vel.Y = Math.Abs(vel.Y) * Restitution;
        }

        entity.Position = pos;
        entity.Velocity = vel;
    }
    #endregion
}
