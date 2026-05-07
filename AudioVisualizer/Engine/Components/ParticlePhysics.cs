using System.Windows;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Physics component for the particle pool.
/// Applies gravity and integrates velocity → position for all live particles.
/// Runs in the fixed-timestep physics loop, keeping the pool class itself
/// focused solely on allocation (Object Pool pattern).
/// </summary>
public sealed class ParticlePhysics : IPhysicsSystem
{
    #region Fields
    /// <summary>
    /// The particle pool whose live particles this component acts on.
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
    /// Create a particle physics component operating on the given pool.
    /// </summary>
    /// <param name="pool">The pool whose particles receive physics updates.</param>
    public ParticlePhysics(ParticlePool pool)
    {
        _pool = pool;
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void ApplyForces(float dt)
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
    public void Integrate(float dt)
    {
        var buffer = _pool.Buffer;
        for (int i = 0; i < buffer.Length; i++)
        {
            ref var p = ref buffer[i];
            if (p.FramesLeft <= 0) continue;

            p.Position += p.Velocity * dt;
        }
    }

    /// <inheritdoc />
    public void ResolveCollisions(float dt)
    {
        // Particles have no collision surfaces — no-op.
    }
    #endregion
}
