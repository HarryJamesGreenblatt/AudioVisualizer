using System;
using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer.Engine;

/// <summary>
/// Thin shell representing a game-world object. Holds shared state (position, velocity)
/// and optional components that plug in physics, rendering, and audio-reactivity.
/// Communication between decoupled systems happens via the <see cref="Collision"/> event (Observer pattern).
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
    /// Optional physics component responsible for gravity, collision, and integration.
    /// </summary>
    public IPhysicsComponent? Physics { get; init; }

    /// <summary>
    /// Optional render component responsible for drawing the entity each frame.
    /// </summary>
    public IRenderComponent? Render { get; init; }

    /// <summary>
    /// Optional audio-reactive component that maps frequency band data to entity state.
    /// </summary>
    public IAudioReactiveComponent? AudioReactive { get; init; }
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
    /// Update Method pattern: advance entity state for one tick.
    /// Audio-reactive runs first so bar heights are fresh for physics collision.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    public void Update(float dt, ReadOnlySpan<float> bands)
    {
        AudioReactive?.React(this, bands);
        Physics?.Update(this, dt);
    }

    /// <summary>
    /// Render this entity to the given drawing context.
    /// </summary>
    /// <param name="dc">WPF drawing context for immediate-mode rendering.</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    public void Draw(DrawingContext dc, Size viewport)
    {
        Render?.Render(this, dc, viewport);
    }

    /// <summary>
    /// Fire collision notification to all observers.
    /// Called by physics components when overlap is detected.
    /// </summary>
    /// <param name="info">Contact point, normal, and impulse data.</param>
    internal void NotifyCollision(CollisionInfo info) => Collision?.Invoke(this, info);
    #endregion
}
