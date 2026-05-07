namespace AudioVisualizer.Engine;

/// <summary>
/// Scene-level physics system contract. Declares the canonical simulation pipeline
/// that every physics concern must implement: forces → integration → collision.
/// Scene calls each phase in order across all registered systems every fixed tick.
/// </summary>
public interface IPhysicsSystem
{
    /// <summary>
    /// Gravitational acceleration used by this system, in system-appropriate units
    /// (px/tick² for peaks, px/s² for particles, etc.).
    /// Exposed so tuning and diagnostics can inspect the value without coupling to internals.
    /// </summary>
    float Gravity { get; }

    /// <summary>
    /// Coefficient of restitution: fraction of impact velocity preserved on bounce.
    /// 0 = perfectly inelastic (dead stop), 1 = perfectly elastic (full bounce).
    /// Systems with no collision surface return 0.
    /// </summary>
    float Restitution { get; }

    /// <summary>
    /// Phase 1: Accumulate external forces (gravity, springs, drag) into velocity.
    /// No position changes yet — only velocity is modified.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void ApplyForces(float dt);

    /// <summary>
    /// Phase 2: Integrate velocity into position.
    /// Called after all forces are accumulated so the full velocity delta is applied at once.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void Integrate(float dt);

    /// <summary>
    /// Phase 3: Detect and resolve collisions / constraints.
    /// May modify both position (depenetration) and velocity (restitution).
    /// Systems with no collision geometry implement this as a no-op.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void ResolveCollisions(float dt);
}
