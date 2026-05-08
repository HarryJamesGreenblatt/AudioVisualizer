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
    /// Gravitational acceleration used by this component, in px/s².
    /// Default 0 (no gravity). Override to opt in. This is acceleration, not force —
    /// it is multiplied by <see cref="Mass"/> when added to the force accumulator so the
    /// underlying simulation stays Newtonian (F = m·a) and Galilean (mass cancels for free-fall).
    /// </summary>
    public virtual float Gravity => 0f;

    /// <summary>
    /// Coefficient of restitution: fraction of impact velocity preserved on bounce.
    /// 0 = perfectly inelastic, 1 = perfectly elastic. Default 0 (no bounce).
    /// </summary>
    public virtual float Restitution => 0f;

    /// <summary>
    /// Inertial mass in arbitrary units (default 1.0). Used by <see cref="IntegrateAccumulated"/>
    /// to convert accumulated forces into acceleration via Newton's 2nd law (a = F/m).
    /// Mass also scales gravitational force so heavier bodies experience the same free-fall acceleration
    /// as lighter ones — distinct values only matter once non-gravitational forces are introduced.
    /// </summary>
    public double Mass { get; set; } = 1.0;

    /// <summary>
    /// Current rotation in degrees. Shared angular state for any physics behavior
    /// that spins (balls, debris, spinning particles). Behaviors that don't rotate
    /// simply leave this at 0; the renderer will apply an identity transform.
    /// </summary>
    public double Rotation { get; set; }

    /// <summary>
    /// Angular velocity in degrees per second. Subclasses that rotate update this in
    /// <see cref="ApplyForces"/> and call <see cref="IntegrateRotation"/> from <see cref="Integrate"/>.
    /// </summary>
    public double AngularVelocity { get; set; }

    /// <summary>Accumulated continuous forces this tick (cleared by <see cref="IntegrateAccumulated"/>).</summary>
    private Vector _forceAccum;

    /// <summary>Accumulated instantaneous impulses this tick (cleared by <see cref="IntegrateAccumulated"/>).</summary>
    private Vector _impulseAccum;

    /// <summary>Accumulated continuous torque this tick (cleared by <see cref="IntegrateAccumulated"/>).</summary>
    private double _torqueAccum;

    /// <summary>Accumulated instantaneous angular impulses this tick (cleared by <see cref="IntegrateAccumulated"/>).</summary>
    private double _angularImpulseAccum;
    #endregion

    #region Force API
    /// <summary>
    /// Add a continuous linear force (units: mass·px/s²). Integrated over <c>dt</c>.
    /// Use for gravity, springs, drag, wind — anything that acts continuously.
    /// </summary>
    public void AddForce(Vector force) => _forceAccum += force;

    /// <summary>
    /// Add an instantaneous linear impulse (units: mass·px/s). Applied directly to velocity
    /// without dt scaling — use for hits, kicks, bounces, anything that should change velocity at a moment in time.
    /// </summary>
    public void AddImpulse(Vector impulse) => _impulseAccum += impulse;

    /// <summary>Add a continuous torque (units: mass·deg/s² in our scaled angular system).</summary>
    public void AddTorque(double torque) => _torqueAccum += torque;

    /// <summary>Add an instantaneous angular impulse (units: mass·deg/s).</summary>
    public void AddAngularImpulse(double angularImpulse) => _angularImpulseAccum += angularImpulse;
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

    /// <summary>
    /// Integrate <see cref="AngularVelocity"/> into <see cref="Rotation"/>, normalizing the
    /// result into [0, 360). Subclasses that want rotation call this from their
    /// <see cref="Integrate"/> override after updating linear position.
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    protected void IntegrateRotation(float dt)
    {
        Rotation += AngularVelocity * dt;
        // Normalize into [0, 360) without unbounded growth
        Rotation %= 360.0;
        if (Rotation < 0) Rotation += 360.0;
    }

    /// <summary>
    /// Newtonian semi-implicit Euler integrator. Consumes the accumulated forces and impulses,
    /// converts them to velocity/position deltas via F = m·a, then clears the accumulators.
    ///
    /// Subclasses that follow standard mechanics call this once from <see cref="Integrate"/>
    /// after their <see cref="ApplyForces"/> override has populated the accumulators.
    /// </summary>
    /// <param name="entity">Entity whose Position and Velocity will be advanced.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    protected void IntegrateAccumulated(SceneEntity entity, float dt)
    {
        double invMass = Mass > 0 ? 1.0 / Mass : 0;

        // Linear: a = F/m → Δv = a·dt + impulse/m → Δx = v·dt   (semi-implicit Euler)
        var deltaV = _forceAccum * (invMass * dt) + _impulseAccum * invMass;
        var newVel = entity.Velocity + deltaV;
        entity.Velocity = newVel;
        entity.Position += newVel * dt;

        // Angular: same shape, scalar.  (Treats Mass as moment of inertia for now — fine
        // until a Phase B pass introduces proper I = k·m·r² per-shape.)
        double angularDeltaV = _torqueAccum * (invMass * dt) + _angularImpulseAccum * invMass;
        AngularVelocity += angularDeltaV;
        IntegrateRotation(dt);

        _forceAccum = default;
        _impulseAccum = default;
        _torqueAccum = 0;
        _angularImpulseAccum = 0;
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

        /// <summary>Bar reactivity providing live column heights as the floor surface.</summary>
        private readonly ReactivityComponent.Bar? _bars;

        /// <summary>Optional peak physics providing additional thin floor surfaces atop the bars.</summary>
        private readonly PhysicsComponent.Peak? _peaks;

        /// <inheritdoc />
        public override float Gravity => 800f;

        /// <inheritdoc />
        public override float Restitution => 0.7f;

        /// <summary>
        /// Fraction of horizontal velocity transferred into spin on each floor bounce.
        /// 0 = frictionless ice, 1 = perfect grip. Real beach balls are around 0.5–0.7.
        /// </summary>
        private const float FloorFriction = 0.6f;

        /// <summary>
        /// How strongly horizontal motion induces rolling-style spin. Larger = spinnier.
        /// Tuned so the visible stripes feel like they're tracking ground contact.
        /// </summary>
        private const float RollCoupling = 1.5f;

        /// <summary>
        /// Per-second multiplicative decay applied to angular velocity (air drag on spin).
        /// </summary>
        private const float AngularDrag = 0.5f;

        /// <summary>
        /// Linear drag coefficient (1/s). F_drag = -c·m·v. About 0.6 gives the same
        /// terminal feel as the previous 1%-per-60fps multiplicative damp.
        /// </summary>
        private const float LinearDrag = 0.6f;

        /// <summary>
        /// Minimum closing-velocity (px/s) between ball and surface required to produce a
        /// real bounce. Below this, the ball matches the surface velocity (rides) instead
        /// of bouncing — prevents micro-jitter on resting contact and stops slow surface
        /// growth from fake-bouncing the ball.
        /// </summary>
        private const double SurfaceBounceThreshold = 30.0;

        /// <summary>
        /// Create a beach-ball physics component. Pass a bar reactivity (and optionally peak
        /// physics) to make the ball collide with the visible spectrum geometry; pass null for
        /// both to get a viewport-only beach ball.
        /// </summary>
        public Ball(double radius = 40, ReactivityComponent.Bar? bars = null, PhysicsComponent.Peak? peaks = null)
        {
            Radius = radius;
            _bars = bars;
            _peaks = peaks;
        }

        /// <inheritdoc />
        public override void ApplyForces(SceneEntity entity, float dt)
        {
            // Gravity as a real force: F_grav = m · g (mass cancels in integration, but the
            // accumulator pattern is preserved so wind/springs/buoyancy can compose linearly later).
            AddForce(new Vector(0, Gravity * Mass));

            // Linear air drag: F_drag = -c·v.  Coefficient tuned to ~1% energy loss per 60fps frame.
            AddForce(-entity.Velocity * (LinearDrag * Mass));

            // Angular drag stays in velocity-space (multiplicative form is numerically more
            // stable for arbitrary dt than an exponential force).
            AngularVelocity *= Math.Pow(1.0 - AngularDrag, dt);
        }

        /// <inheritdoc />
        public override void Integrate(SceneEntity entity, float dt)
        {
            IntegrateAccumulated(entity, dt);
        }

        /// <inheritdoc />
        public override void ResolveCollisions(SceneEntity entity, float dt, Size viewport)
        {
            var pos = entity.Position;
            var vel = entity.Velocity;
            double preBounceVx = vel.X;
            double preBounceVy = vel.Y;

            bool wallHit = BounceInsideViewport(ref pos, ref vel, Radius, viewport, Restitution, settleSpeed: 50);

            // Bar/peak collision: treat the tallest column under the ball as the floor.
            // Resolved after wall clamp so the ball is already inside the viewport.
            bool surfaceHit = ResolveSurfaceCollision(ref pos, ref vel, viewport);

            entity.Position = pos;
            entity.Velocity = vel;

            // Floor / ceiling impact: transfer horizontal velocity into spin (rolling friction).
            // omega (deg/sec) for rolling without slipping = (v / r) * (180/PI), scaled by coupling.
            if ((wallHit || surfaceHit) && Math.Abs(preBounceVy) > 1)
            {
                double rollOmega = (preBounceVx / Radius) * (180.0 / Math.PI) * RollCoupling;
                AngularVelocity = AngularVelocity * (1 - FloorFriction) + rollOmega * FloorFriction;
            }

            // Side-wall impact: vertical velocity translates into spin (climbing/sliding).
            if (wallHit && Math.Abs(preBounceVx) > 1 && (pos.X <= Radius + 0.5 || pos.X >= viewport.Width - Radius - 0.5))
            {
                double wallOmega = (-preBounceVy / Radius) * (180.0 / Math.PI) * RollCoupling;
                if (pos.X <= Radius + 0.5) wallOmega = -wallOmega;
                AngularVelocity = AngularVelocity * (1 - FloorFriction) + wallOmega * FloorFriction;
            }
        }

        /// <summary>
        /// Treat the bars (and optionally peaks) as a live, dynamic floor surface.
        /// For each column the ball overlaps, find the tallest bar/peak top; the highest
        /// such top across all overlapping columns is the floor for this tick. The bar's
        /// own surface velocity is used for proper relative-velocity collision response
        /// (so a rising bar launches the ball, not just stops it).
        /// </summary>
        /// <returns>True if the ball was in contact with a surface this tick.</returns>
        private bool ResolveSurfaceCollision(ref Point pos, ref Vector vel, Size viewport)
        {
            if (_bars == null) return false;
            var heights = _bars.BarHeights;
            if (heights.Length == 0 || viewport.Width <= 0) return false;

            double colWidth = viewport.Width / heights.Length;
            if (colWidth <= 0) return false;

            // Columns the ball's AABB overlaps
            int firstCol = (int)Math.Floor((pos.X - Radius) / colWidth);
            int lastCol  = (int)Math.Floor((pos.X + Radius) / colWidth);
            if (firstCol < 0) firstCol = 0;
            if (lastCol  >= heights.Length) lastCol = heights.Length - 1;
            if (firstCol > lastCol) return false;

            // Tallest surface in pixels (measured from the floor up) and ITS y-velocity.
            // Heights grow upward → a positive dHeight/dt means surface moves −y on screen.
            float tallest = 0f;
            float surfaceVy = 0f;
            var peakHeights = _peaks?.PeakHeights;
            var barVelocities = _bars.BarSurfaceVelocities;

            for (int i = firstCol; i <= lastCol; i++)
            {
                float h = heights[i];
                float vy = i < barVelocities.Length ? barVelocities[i] : 0f;

                if (peakHeights != null && i < peakHeights.Length && peakHeights[i] > h)
                {
                    h = peakHeights[i];
                    vy = 0f; // peaks don't currently expose their own surface velocity
                }

                if (h > tallest) { tallest = h; surfaceVy = vy; }
            }
            if (tallest <= 0) return false;

            double surfaceY = viewport.Height - tallest;
            double ballBottom = pos.Y + Radius;
            if (ballBottom <= surfaceY) return false; // no overlap

            // Depenetrate: snap ball to sit on the surface.
            pos.Y = surfaceY - Radius;

            // Relative-velocity collision response.
            //   vRel = vBall − vSurface       (positive = closing in screen coords)
            //   vBall_after = vSurface − R·vRel  (Newton's restitution against a moving wall)
            // This makes a rising bar transfer its momentum into the ball, instead of zeroing
            // the ball's velocity and "sticking" it to the bar top.
            double vRel = vel.Y - surfaceVy;

            if (vRel > SurfaceBounceThreshold)
                vel.Y = surfaceVy - Restitution * vRel;
            else if (vRel > 0)
                vel.Y = surfaceVy;   // slow contact — ride the surface, no fake bounce
            // else: ball already moving up faster than the surface — leave it alone

            return true;
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
