using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine;

/// <summary>
/// Thin shell representing a game-world object. Owns shared state (position, velocity)
/// and three optional components — Reactivity, Physics, Rendering — each encapsulating
/// one concern. Subclasses wire concrete components in their constructors; the base
/// class orchestrates the per-tick component pipeline.
/// </summary>
public class SceneEntity
{
    #region Properties
    /// <summary>
    /// Pan-domain shared state: world position of the entity.
    /// </summary>
    public Point Position { get; set; }

    /// <summary>
    /// Pan-domain shared state: velocity vector applied by physics each tick.
    /// </summary>
    public Vector Velocity { get; set; }

    /// <summary>
    /// Whether the entity is still alive. Dead entities are removed at end of frame.
    /// </summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>
    /// Reactivity component: maps audio band data to entity state.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public ReactivityComponent? Reactivity { get; protected set; }

    /// <summary>
    /// Physics component: forces, integration, and collision for this entity.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public PhysicsComponent? Physics { get; protected set; }

    /// <summary>
    /// Rendering component: draws the entity each frame.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public RenderingComponent? Rendering { get; protected set; }
    #endregion

    #region Events
    /// <summary>
    /// Observer pattern: fired when a collision is detected by the physics component.
    /// Subscribers receive the originating entity and contact metadata.
    /// </summary>
    public event Action<SceneEntity, CollisionInfo>? Collision;
    #endregion

    #region Methods
    /// <summary>
    /// Update Method pattern: advance entity state for one fixed-timestep tick.
    /// Reactivity runs first (audio → state), then physics phases 1+2 (forces, integration).
    /// Phase 3 (collision) runs separately via <see cref="ResolveCollisions"/> so the scene
    /// can interleave inter-entity collision after all entities have integrated.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    public virtual void Update(float dt, ReadOnlySpan<float> bands, Size viewport)
    {
        Reactivity?.React(this, bands, viewport, dt);
        Physics?.ApplyForces(this, dt);
        Physics?.Integrate(this, dt);
    }

    /// <summary>
    /// Run the physics collision phase. Separated from <see cref="Update"/> so the scene
    /// can run all entity integrations first, then resolve collisions consistently.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    public virtual void ResolveCollisions(float dt, Size viewport)
    {
        Physics?.ResolveCollisions(this, dt, viewport);
    }

    /// <summary>
    /// Render this entity to the given drawing context.
    /// </summary>
    /// <param name="dc">WPF drawing context for immediate-mode rendering.</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    public virtual void Draw(DrawingContext dc, Size viewport)
    {
        Rendering?.Render(this, dc, viewport);
    }

    /// <summary>
    /// Fire collision notification to all observers.
    /// Called by physics components when contact is detected.
    /// </summary>
    /// <param name="info">Contact point, normal, and impulse data.</param>
    internal void NotifyCollision(CollisionInfo info) => Collision?.Invoke(this, info);
    #endregion
}
