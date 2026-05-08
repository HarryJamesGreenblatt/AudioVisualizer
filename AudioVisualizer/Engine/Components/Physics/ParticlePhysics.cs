using System.Windows;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer.Engine.Components.Physics;

/// <summary>
/// Physics component for the particle pool entity.
/// Applies gravity and integrates velocity → position for all live particles.
/// Operates on the pool's struct buffer directly to avoid per-particle dispatch overhead
/// — each particle is data, not an entity, so batch processing preserves cache locality.
/// </summary>
public sealed class ParticlePhysics : IPhysicsComponent
{
    #region Fields
    /// <summary>
    /// The pool entity whose particle buffer this component acts on.
    /// </summary>
    private readonly ParticlePool _pool;
    #endregion

    #region Properties
    /// <inheritdoc />
    public float Gravity => 300f;

    /// <inheritdoc />
    public float Restitution => 0f;
    #endregion

    #region Constructor
    /// <summary>
    /// Create a particle physics component operating on the given pool's buffer.
    /// </summary>
    /// <param name="pool">The pool entity whose particles receive physics updates.</param>
    public ParticlePhysics(ParticlePool pool)
    {
        _pool = pool;
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void ApplyForces(SceneEntity entity, float dt)
    {
        var buffer = _pool.Buffer;
        for (int i = 0; i < buffer.Length; i++)
        {
            ref var p = ref buffer[i];
            if (p.FramesLeft <= 0) continue;
            p.Velocity += new Vector(0, Gravity * dt);
        }
    }

    /// <inheritdoc />
    public void Integrate(SceneEntity entity, float dt)
    {
        var buffer = _pool.Buffer;
        for (int i = 0; i < buffer.Length; i++)
        {
            ref var p = ref buffer[i];
            if (p.FramesLeft <= 0) continue;
            p.Position += p.Velocity * dt;
        }

        // Lifecycle tick belongs here (post-integration), not in the pool itself.
        _pool.TickLifetimes();
    }

    /// <inheritdoc />
    public void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
    {
        // Particles have no collision surfaces — no-op.
    }
    #endregion
}
