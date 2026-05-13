using System;
using System.Windows;
using System.Windows.Media;
using WavBall.Components;
using WavBall.Models;

namespace WavBall;

/// <summary>
/// Thin shell representing a game-world object. Owns shared state (position, velocity)
/// and three optional components — Reactivity, Physics, Rendering — each encapsulating
/// one concern. Subclasses wire concrete components in their constructors; the base
/// class orchestrates the per-tick component pipeline.
/// </summary>
public class World
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
    /// When true, physics components skip force application and integration for this entity.
    /// Used by <see cref="Components.Input.Drag"/> to suspend simulation while the
    /// user is holding the entity, and by any "frozen" / paused state machine.
    /// </summary>
    public bool IsKinematic { get; set; }

    /// <summary>
    /// Input component: translates user input (mouse, keys) into entity-state mutations.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public Components.Input? Input { get; protected set; }

    /// <summary>
    /// Reactivity component: maps audio band data to entity state.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public Reactivity? Reactivity { get; protected set; }

    /// <summary>
    /// Physics component: forces, integration, and collision for this entity.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public Physics? Physics { get; protected set; }

    /// <summary>
    /// Rendering component: draws the entity each frame.
    /// Settable from subclass constructors via protected setter.
    /// </summary>
    public Rendering? Rendering { get; protected set; }
    #endregion

    #region Events
    /// <summary>
    /// Observer pattern: fired when a collision is detected by the physics component.
    /// Subscribers receive the originating entity and contact metadata.
    /// </summary>
    public event Action<World, CollisionInfo>? Collision;
    #endregion

    #region Methods
    /// <summary>
    /// Update Method pattern: advance entity state for one fixed-timestep tick.
    /// Pipeline order: Input → Reactivity → Physics (forces → integration). Collision
    /// runs separately via <see cref="ResolveCollisions"/> so the scene can interleave
    /// inter-entity collision after all entities have integrated.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    /// <param name="mouse">Mouse snapshot for this tick.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    public virtual void Update(float dt, MouseState mouse, ReadOnlySpan<float> bands, Size viewport)
    {
        Input?.Update(this, mouse, viewport, dt);
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
