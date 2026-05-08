using System.Windows;

namespace AudioVisualizer.Engine;

/// <summary>
/// Per-entity physics component. Each entity owns its own physics behavior,
/// hooked into the canonical 3-phase pipeline: forces → integration → collision.
/// Replaces the scene-level <c>IPhysicsSystem</c> strategy with strict Component pattern:
/// physics is a property of the entity, not a global system iterating over collections.
/// </summary>
public interface IPhysicsComponent
{
    /// <summary>
    /// Gravitational acceleration used by this component, in component-appropriate units.
    /// Exposed so tuning and diagnostics can inspect the value without coupling to internals.
    /// </summary>
    float Gravity { get; }

    /// <summary>
    /// Coefficient of restitution: fraction of impact velocity preserved on bounce.
    /// 0 = perfectly inelastic, 1 = perfectly elastic. Components without collision return 0.
    /// </summary>
    float Restitution { get; }

    /// <summary>
    /// Phase 1: Accumulate external forces (gravity, springs, drag) into velocity.
    /// No position changes yet — only velocity is modified.
    /// </summary>
    /// <param name="entity">The owning entity whose velocity will be mutated.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void ApplyForces(SceneEntity entity, float dt);

    /// <summary>
    /// Phase 2: Integrate velocity into position.
    /// Called after all forces are accumulated so the full velocity delta is applied at once.
    /// </summary>
    /// <param name="entity">The owning entity whose position will be mutated.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    void Integrate(SceneEntity entity, float dt);

    /// <summary>
    /// Phase 3: Detect and resolve collisions / constraints.
    /// May modify both position (depenetration) and velocity (restitution).
    /// Components without collision geometry implement this as a no-op.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be corrected.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    /// <param name="viewport">Current viewport dimensions for boundary collision.</param>
    void ResolveCollisions(SceneEntity entity, float dt, Size viewport);
}
