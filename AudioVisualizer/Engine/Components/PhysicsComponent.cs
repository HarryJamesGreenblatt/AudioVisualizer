using System;
using System.Windows;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Abstract base for all physics behaviors. Defines the canonical 3-phase pipeline
/// (forces → integration → collision) as virtual methods with no-op defaults, so
/// subclasses override only the phases they care about.
///
/// Concrete behaviors are nested types — <see cref="Ball"/>, <see cref="Peak"/>,
/// <see cref="Particle"/> — so the entire physics surface lives in one file and
/// the type system enforces "X is a kind of physics" via <c>PhysicsComponent.X</c>.
/// </summary>
public abstract class PhysicsComponent
{
    #region Properties
    /// <summary>
    /// Gravitational acceleration used by this component, in component-appropriate units.
    /// Default 0 (no gravity). Override to opt in.
    /// </summary>
    public virtual float Gravity => 0f;

    /// <summary>
    /// Coefficient of restitution: fraction of impact velocity preserved on bounce.
    /// 0 = perfectly inelastic, 1 = perfectly elastic. Default 0 (no bounce).
    /// </summary>
    public virtual float Restitution => 0f;
    #endregion

    #region Pipeline
    /// <summary>
    /// Phase 1: Accumulate external forces (gravity, springs, drag) into velocity.
    /// Default no-op.
    /// </summary>
    public virtual void ApplyForces(SceneEntity entity, float dt) { }

    /// <summary>
    /// Phase 2: Integrate velocity into position. Default no-op.
    /// </summary>
    public virtual void Integrate(SceneEntity entity, float dt) { }

    /// <summary>
    /// Phase 3: Detect and resolve collisions / constraints. Default no-op.
    /// </summary>
    public virtual void ResolveCollisions(SceneEntity entity, float dt, Size viewport) { }
    #endregion

    #region Helpers
    /// <summary>
    /// Resolve an axis-aligned bounding-box collision against the viewport rect.
    /// Mutates <paramref name="pos"/> and <paramref name="vel"/> in place; returns true if any wall was hit.
    /// Reusable across any spherical/AABB physics behavior that needs viewport containment.
    /// </summary>
    /// <param name="pos">World-space center; clamped to keep the AABB inside the viewport.</param>
    /// <param name="vel">Velocity; reflected with restitution on hit walls.</param>
    /// <param name="halfExtent">Half-width/height of the entity (e.g., ball radius).</param>
    /// <param name="viewport">Viewport size.</param>
    /// <param name="restitution">Fraction of velocity preserved on bounce (0–1).</param>
    /// <param name="settleSpeed">Floor-bounce velocities below this magnitude are zeroed to prevent jitter.</param>
    protected static bool BounceInsideViewport(
        ref Point pos, ref Vector vel, double halfExtent, Size viewport,
        float restitution, double settleSpeed = 0)
    {
        if (viewport.Width <= 0 || viewport.Height <= 0) return false;

        bool hit = false;

        if (pos.X - halfExtent < 0)            { pos.X = halfExtent;                  vel.X =  Math.Abs(vel.X) * restitution; hit = true; }
        else if (pos.X + halfExtent > viewport.Width)  { pos.X = viewport.Width  - halfExtent; vel.X = -Math.Abs(vel.X) * restitution; hit = true; }

        if (pos.Y + halfExtent > viewport.Height)
        {
            pos.Y = viewport.Height - halfExtent;
            vel.Y = -Math.Abs(vel.Y) * restitution;
            if (settleSpeed > 0 && Math.Abs(vel.Y) < settleSpeed) vel.Y = 0;
            hit = true;
        }
        else if (pos.Y - halfExtent < 0)
        {
            pos.Y = halfExtent;
            vel.Y = Math.Abs(vel.Y) * restitution;
            hit = true;
        }

        return hit;
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Ball
    /// <summary>
    /// Beach-ball physics: gravity + air drag + viewport wall bouncing.
    /// </summary>
    public sealed class Ball : PhysicsComponent
    {
        /// <summary>
        /// Ball radius in pixels for collision detection.
        /// </summary>
        public double Radius { get; }

        /// <inheritdoc />
        public override float Gravity => 800f;

        /// <inheritdoc />
        public override float Restitution => 0.7f;

        /// <summary>
        /// Create a beach-ball physics component with the given radius.
        /// </summary>
        public Ball(double radius = 40) { Radius = radius; }

        /// <inheritdoc />
        public override void ApplyForces(SceneEntity entity, float dt)
        {
            var vel = entity.Velocity;
            vel.Y += Gravity * dt;
            vel *= Math.Pow(0.99, dt * 60); // ~1% drag per 60fps frame
            entity.Velocity = vel;
        }

        /// <inheritdoc />
        public override void Integrate(SceneEntity entity, float dt)
        {
            entity.Position += entity.Velocity * dt;
        }

        /// <inheritdoc />
        public override void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
        {
            var pos = entity.Position;
            var vel = entity.Velocity;
            BounceInsideViewport(ref pos, ref vel, Radius, viewport, Restitution, settleSpeed: 50);
            entity.Position = pos;
            entity.Velocity = vel;
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Peak
    /// <summary>
    /// Peak-hold physics: each band's marker falls under gravity, holds at impact,
    /// and elastically bounces when bars push back up into it.
    /// Cross-entity coupling: reads live bar heights from a <see cref="ReactivityComponent.Bar"/>.
    /// </summary>
    public sealed class Peak : PhysicsComponent
    {
        private readonly ReactivityComponent.Bar _bars;
        private float[] _peakHold = [];
        private float[] _peakVelocity = [];
        private int[] _holdTimer = [];

        private const int HoldTicks = 30;
        private const float BounceThreshold = 0.5f;

        /// <summary>
        /// Peak heights in pixels per band, readable by the renderer.
        /// </summary>
        public float[] PeakHeights => _peakHold;

        /// <inheritdoc />
        public override float Gravity => 0.06f;

        /// <inheritdoc />
        public override float Restitution => 0.3f;

        /// <summary>
        /// Create a peak-hold physics component coupled to the given bar reactivity.
        /// </summary>
        public Peak(ReactivityComponent.Bar bars) { _bars = bars; }

        private bool EnsureBuffers()
        {
            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return false;
            if (_peakHold.Length != barHeights.Length)
            {
                _peakHold     = new float[barHeights.Length];
                _peakVelocity = new float[barHeights.Length];
                _holdTimer    = new int  [barHeights.Length];
            }
            return true;
        }

        /// <inheritdoc />
        public override void ApplyForces(SceneEntity entity, float dt)
        {
            if (!EnsureBuffers()) return;
            for (int i = 0; i < _peakHold.Length; i++)
            {
                if (_holdTimer[i] > 0) _holdTimer[i]--;
                else                    _peakVelocity[i] += Gravity;
            }
        }

        /// <inheritdoc />
        public override void Integrate(SceneEntity entity, float dt)
        {
            if (_peakHold.Length == 0) return;
            for (int i = 0; i < _peakHold.Length; i++)
            {
                if (_holdTimer[i] > 0) continue;
                _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
            }
        }

        /// <inheritdoc />
        public override void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
        {
            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return;

            for (int i = 0; i < barHeights.Length; i++)
            {
                if (barHeights[i] >= _peakHold[i])
                {
                    if (_peakVelocity[i] > BounceThreshold)
                    {
                        // Falling peak hit the bar → elastic bounce
                        _peakHold[i] = barHeights[i];
                        _peakVelocity[i] = -(_peakVelocity[i] * Restitution);
                        _holdTimer[i] = 0;
                    }
                    else
                    {
                        // Bar rose into peak, or bounce decayed → settle and hold
                        _peakHold[i] = barHeights[i];
                        _peakVelocity[i] = 0f;
                        _holdTimer[i] = HoldTicks;
                    }
                }
            }
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Particle
    /// <summary>
    /// Particle-pool physics: batch gravity + integration over the pool's struct buffer
    /// for cache locality and zero per-particle dispatch overhead.
    /// Also drives lifecycle (TickLifetimes) post-integration.
    /// </summary>
    public sealed class Particle : PhysicsComponent
    {
        private readonly ParticlePool _pool;

        /// <inheritdoc />
        public override float Gravity => 300f;

        /// <summary>
        /// Create a particle physics component operating on the given pool's buffer.
        /// </summary>
        public Particle(ParticlePool pool) { _pool = pool; }

        /// <inheritdoc />
        public override void ApplyForces(SceneEntity entity, float dt)
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
        public override void Integrate(SceneEntity entity, float dt)
        {
            var buffer = _pool.Buffer;
            for (int i = 0; i < buffer.Length; i++)
            {
                ref var p = ref buffer[i];
                if (p.FramesLeft <= 0) continue;
                p.Position += p.Velocity * dt;
            }
            // Lifecycle tick belongs here, not in the pool itself.
            _pool.TickLifetimes();
        }
    }
    #endregion
}
