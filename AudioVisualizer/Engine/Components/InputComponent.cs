using System;
using System.Windows;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Abstract base for all user-input behaviors. Subclasses override <see cref="Update"/>
/// to translate mouse state into entity state (drag, click, sculpt, etc.).
///
/// Concrete behaviors are nested types so the entire input surface lives in one file,
/// matching the layout of the other component bases (<see cref="PhysicsComponent"/>,
/// <see cref="ReactivityComponent"/>, <see cref="RenderingComponent"/>).
/// </summary>
public abstract class InputComponent
{
    #region Pipeline
    /// <summary>
    /// Translate the current <see cref="MouseState"/> into entity-state mutations.
    /// Called before reactivity/physics each tick. Default no-op so subclasses opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="mouse">Mouse snapshot for this tick.</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    public virtual void Update(SceneEntity entity, MouseState mouse, Size viewport, float dt) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Drag
    /// <summary>
    /// Click-and-drag input: when the user presses on the owning entity, it becomes
    /// kinematic (physics suspended) and follows the cursor; on release, throws with
    /// the cursor's recent velocity so flicking feels natural.
    ///
    /// Hit-testing is circular against the sibling <see cref="PhysicsComponent.Ball"/>'s
    /// radius. Future variants for AABB hit-tests can sit beside this as another nested class.
    /// </summary>
    public sealed class Drag : InputComponent
    {
        #region Fields
        /// <summary>Sibling physics ref \u2014 we need the radius for hit-testing.</summary>
        private readonly PhysicsComponent.Ball _physics;

        /// <summary>Whether this entity is currently grabbed.</summary>
        private bool _grabbed;

        /// <summary>Cursor position last tick \u2014 used to derive throw velocity on release.</summary>
        private Point _lastCursor;

        /// <summary>Smoothed cursor velocity (px/s), updated each tick during a drag.</summary>
        private Vector _cursorVelocity;

        /// <summary>Offset from cursor to entity center at grab time, so the ball doesn't snap to the cursor.</summary>
        private Vector _grabOffset;

        /// <summary>Maximum throw speed (px/s). Caps absurd flick velocities.</summary>
        private const double MaxThrowSpeed = 2500.0;

        /// <summary>Smoothing factor for cursor velocity (one-pole IIR). Higher = more responsive but jitterier.</summary>
        private const double VelocitySmoothing = 0.35;
        #endregion

        #region Constructor
        /// <summary>Create a drag-input component coupled to the given ball physics for hit-testing.</summary>
        public Drag(PhysicsComponent.Ball physics) { _physics = physics; }
        #endregion

        #region Methods
        /// <inheritdoc />
        public override void Update(SceneEntity entity, MouseState mouse, Size viewport, float dt)
        {
            // Press → grab if cursor is over the ball
            if (mouse.JustPressed && !_grabbed)
            {
                var d = mouse.Position - entity.Position;
                if (d.LengthSquared <= _physics.Radius * _physics.Radius)
                {
                    _grabbed = true;
                    _grabOffset = entity.Position - mouse.Position;
                    _lastCursor = mouse.Position;
                    _cursorVelocity = default;
                    entity.IsKinematic = true;
                    // Snap velocity to zero so the ball doesn't carry pre-grab momentum
                    entity.Velocity = default;
                }
            }

            // Hold → follow cursor and track velocity
            if (_grabbed && mouse.IsDown)
            {
                entity.Position = mouse.Position + _grabOffset;

                if (dt > 0)
                {
                    var instant = (mouse.Position - _lastCursor) / dt;
                    _cursorVelocity = (1 - VelocitySmoothing) * _cursorVelocity + VelocitySmoothing * instant;
                }
                _lastCursor = mouse.Position;
            }

            // Release → throw with smoothed cursor velocity
            if (_grabbed && (mouse.JustReleased || !mouse.IsDown))
            {
                _grabbed = false;
                entity.IsKinematic = false;

                var v = _cursorVelocity;
                double speed = v.Length;
                if (speed > MaxThrowSpeed) v *= MaxThrowSpeed / speed;
                entity.Velocity = v;

                _cursorVelocity = default;
            }
        }
        #endregion
    }
    #endregion
}
