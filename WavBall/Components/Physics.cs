using System;
using System.Windows;
using WavBall.Configuration;
using WavBall.Entities;
using WavBall.Models;

namespace WavBall.Components;

/// <summary>
/// Abstract base for all physics behaviors. Defines the canonical 3-phase pipeline
/// (forces → integration → collision) as virtual methods with no-op defaults, so
/// subclasses override only the phases they care about.
///
/// Concrete behaviors are nested types — <see cref="Ball"/>, <see cref="Peak"/>,
/// <see cref="Particle"/> — so the entire physics surface lives in one file and
/// the type system enforces "X is a kind of physics" via <c>PhysicsComponent.X</c>.
/// </summary>
public abstract class Physics
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
    public virtual void ApplyForces(World entity, float dt) { }

    /// <summary>
    /// Phase 2: Integrate velocity into position. Default no-op.
    /// </summary>
    public virtual void Integrate(World entity, float dt) { }

    /// <summary>
    /// Phase 3: Detect and resolve collisions / constraints. Default no-op.
    /// </summary>
    public virtual void ResolveCollisions(World entity, float dt, Size viewport) { }
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
    protected void IntegrateAccumulated(World entity, float dt)
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
    /// Preset-driven ball physics: gravity + air drag + viewport wall bouncing.
    /// All per-ball-type constants come from <see cref="BallPreset"/>; collision
    /// constants remain shared across all types.
    /// </summary>
    public sealed class Ball : Physics
    {
        /// <summary>Ball type identifier — drives rendering dispatch and game-mode progression.</summary>
        public BallKind Kind { get; }

        /// <summary>Ball radius in pixels for collision detection.</summary>
        public double Radius { get; }

        /// <summary>Bar reactivity providing live column heights as the floor surface.</summary>
        private readonly Reactivity.Bar? _bars;

        /// <summary>Optional peak physics providing additional thin floor surfaces atop the bars.</summary>
        private readonly Physics.Peak? _peaks;

        // ── Per-type physics constants (from BallPreset) ──
        private readonly float _gravity;
        private readonly float _restitution;
        private readonly float _floorFriction;
        private readonly float _rollCoupling;
        private readonly float _angularDrag;
        private readonly float _linearDrag;
        private readonly float _quadraticDrag;

        /// <inheritdoc />
        public override float Gravity => _gravity;

        /// <inheritdoc />
        public override float Restitution => _restitution;

        /// <summary>
        /// Minimum closing-velocity (px/s) between ball and surface required to produce a
        /// real bounce. Below this, the ball matches the surface velocity (rides) instead
        /// of bouncing — prevents micro-jitter on resting contact and stops slow surface
        /// growth from fake-bouncing the ball.
        /// </summary>
        private const double SurfaceBounceThreshold = 80.0;

        /// <summary>
        /// Reference closing-velocity (px/s) used to scale velocity-dependent restitution loss.
        /// At |vRel| = this value the effective restitution is reduced by <see cref="RestitutionLossSlope"/>.
        /// </summary>
        private const double RestitutionLossReference = 800.0;

        /// <summary>
        /// How aggressively restitution decays with impact speed. Real materials lose more
        /// energy on harder hits (deformation, heat). 0 = constant restitution, 1 = full loss at the reference speed.
        /// </summary>
        private const double RestitutionLossSlope = 0.6;

        /// <summary>Floor on the velocity-attenuated restitution — stops it collapsing to 0.</summary>
        private const double MinEffectiveRestitution = 0.15;

        /// <summary>
        /// Pinball-bumper boost applied on side-wall impacts. Multiplied by the ball's
        /// closing speed to produce an inward impulse that drives the ball back into play.
        /// Prevents the frequency-distribution bias from parking the ball against a wall.
        /// </summary>
        private const double WallBumperBoost = 0.6;

        /// <summary>
        /// Minimum outbound speed (px/s) the bumper guarantees on a side-wall hit.
        /// Even a gentle drift into the wall produces at least this much kick-back.
        /// </summary>
        private const double WallBumperMinKick = 150.0;

        /// <summary>
        /// Refractory period (seconds) after a surface bounce during which we suppress
        /// further bounces and only depenetrate. Prevents "machine-gunning" when bars
        /// oscillate at audio frame rate against a ball that's barely separating.
        /// </summary>
        private const double BounceRefractorySeconds = 0.05;

        /// <summary>
        /// Lateral kick (as fraction of ball impact velocity) applied when the ball
        /// straddles columns of significantly different heights — simulates the contact
        /// normal tilting away from the taller-side column. Fixes the stuck-X problem.
        /// </summary>
        private const double TiltedNormalKick = 0.5;

        /// <summary>
        /// Height-difference (px) between sampled columns above which the contact normal
        /// is treated as tilted. Below this we keep the normal vertical (cheap stair-step approx).
        /// </summary>
        private const double NormalTiltThreshold = 6.0;

        /// <summary>
        /// Half-width of the tilt sampling window in columns. The ball spans many columns,
        /// so one column over is too local for a sensible local slope. Sampling ±3 gives
        /// a smoother gradient that matches the ball's actual contact patch.
        /// </summary>
        private const int TiltSampleHalfWidth = 3;

        /// <summary>
        /// Maximum raw surface velocity (px/s) passed into the collision formula.
        /// Prevents wildly oscillating bars from producing unbounded impulses.
        /// </summary>
        private const double SurfaceVelocityCap = 1200.0;

        /// <summary>
        /// Reference mass at which the surface fully couples to the ball (1:1 velocity
        /// transfer). Balls heavier than this receive proportionally less impulse from
        /// surface strikes — a bowling ball (mass 15) shrugs off a peak hit that would
        /// launch a beach ball (mass 1). Coupling factor = RefMass / max(Mass, RefMass).
        /// </summary>
        private const double SurfaceCouplingRefMass = 8.0;

        /// <summary>Time (seconds) since last surface bounce — supports the refractory period.</summary>
        private double _bounceRefractory = 0.0;

        /// <summary>
        /// True when the ball is in contact with the bar/peak surface this tick.
        /// Cleared at the start of each ResolveCollisions pass; set by ResolveSurfaceCollision.
        /// Used by game-mode anti-cheat to confirm the ball has touched the surface after release.
        /// </summary>
        public bool HasSurfaceContact { get; private set; }

        /// <summary>
        /// Reference to the active goal entity. When set, alive, and enabled, the ball
        /// experiences a spring-like attraction toward the goal — force proportional to
        /// distance so it's weak near the goal (no suction) but strong when the ball
        /// drifts far away (corrects the rightward spectrum-slope bias).
        /// </summary>
        public World? GoalEntityRef { get; set; }

        /// <summary>
        /// Linear spring constant (px/s² per px of distance). The attraction
        /// acceleration is <c>k · dist</c>, so force is zero at the goal and grows
        /// linearly with distance — a restoring spring, not a gravity well.
        /// </summary>
        private const double GoalAttractionK = 0.25;

        /// <summary>
        /// Maximum acceleration (px/s²) the goal attraction can produce, reached
        /// at dist = MaxAccel / K ≈ 480 px. Beyond that, the pull is constant.
        /// ~15% of gravity — enough to steer, never enough to dominate.
        /// </summary>
        private const double GoalAttractionMaxAccel = 120.0;

        /// <summary>
        /// Create a ball physics component from a preset. Pass a bar reactivity (and optionally
        /// peak physics) to make the ball collide with the visible spectrum geometry.
        /// </summary>
        public Ball(BallPreset preset, Reactivity.Bar? bars = null, Physics.Peak? peaks = null)
        {
            Kind = preset.Kind;
            Radius = preset.Radius;
            Mass = preset.Mass;
            _gravity = preset.Gravity;
            _restitution = preset.Restitution;
            _linearDrag = preset.LinearDrag;
            _quadraticDrag = preset.QuadraticDrag;
            _angularDrag = preset.AngularDrag;
            _floorFriction = preset.FloorFriction;
            _rollCoupling = preset.RollCoupling;
            _bars = bars;
            _peaks = peaks;
        }

        /// <inheritdoc />
        public override void ApplyForces(World entity, float dt)
        {
            // Kinematic entities are externally controlled (e.g. user dragging the ball);
            // skip force/drag accumulation so the input component can dictate position freely.
            if (entity.IsKinematic) return;

            // Gravity as a real force: F_grav = m · g (mass cancels in integration so all
            // balls free-fall identically — the correct Galilean result).
            AddForce(new Vector(0, Gravity * Mass));

            var v = entity.Velocity;
            double speed = v.Length;

            // Linear (viscous) drag: F = -c·v.  NOT mass-scaled — aerodynamic drag depends
            // on shape/size, not mass. Heavier balls decelerate slower (a = F/m = -c·v/m).
            AddForce(-v * _linearDrag);

            // Quadratic (form) drag: F = -c·|v|·v.  Same principle — mass-independent force
            // so heavy balls have higher terminal velocity naturally.
            if (speed > 0.01)
                AddForce(-v * (_quadraticDrag * speed));

            // Gravitational attraction toward the active goal — a linear spring
            // (force ∝ distance) that corrects the rightward drift from the spectrum slope.
            // Weak near the goal (no suction), strong when far away (pulls the ball back).
            // Only active when the goal is visible (respects anti-cheat suppression).
            if (GoalEntityRef is Entities.Goal { Enabled: true, IsAlive: true } goalEnt)
            {
                var diff = goalEnt.Position - entity.Position;
                double dist = diff.Length;
                if (dist > 1.0)
                {
                    double accel = Math.Min(GoalAttractionMaxAccel, GoalAttractionK * dist);
                    var direction = diff / dist;
                    AddForce(direction * (accel * Mass));
                }
            }

            // Angular drag stays in velocity-space and uses the analytic exponential
            // solution to dω/dt = -k·ω, which is well-defined for any positive coefficient
            // (the multiplicative form Math.Pow(1-k, dt) breaks for k ≥ 1).
            AngularVelocity *= Math.Exp(-_angularDrag * dt);
        }

        /// <inheritdoc />
        public override void Integrate(World entity, float dt)
        {
            // Kinematic entities have their position dictated by the input component;
            // we still tick rotation so the ball can spin while held.
            if (entity.IsKinematic)
            {
                IntegrateRotation(dt);
                return;
            }
            IntegrateAccumulated(entity, dt);
        }

        /// <inheritdoc />
        public override void ResolveCollisions(World entity, float dt, Size viewport)
        {
            // Refractory countdown ticks even on no-contact frames.
            if (_bounceRefractory > 0)
                _bounceRefractory = Math.Max(0, _bounceRefractory - dt);

            // Reset surface contact flag each tick; ResolveSurfaceCollision will set it.
            HasSurfaceContact = false;

            // While being dragged, skip all collision response — the user dictates position.
            if (entity.IsKinematic) return;

            var pos = entity.Position;
            var vel = entity.Velocity;
            double preBounceVy = vel.Y;

            bool wallHit = BounceInsideViewport(ref pos, ref vel, Radius, viewport, Restitution, settleSpeed: 50);

            // ── Pinball bumper on side walls ──
            // If the ball just contacted a side wall, apply an extra inward impulse so
            // the frequency-distribution bias doesn't park it against one edge.
            if (wallHit)
            {
                bool leftWall  = pos.X <= Radius + 1.0;
                bool rightWall = pos.X >= viewport.Width - Radius - 1.0;
                if (leftWall || rightWall)
                {
                    double impactSpeed = Math.Abs(vel.X); // post-restitution speed
                    double kick = Math.Max(impactSpeed * WallBumperBoost, WallBumperMinKick);
                    vel.X = leftWall ? Math.Abs(vel.X) + kick : -(Math.Abs(vel.X) + kick);
                }
            }

            // Bar/peak collision: treat the tallest column under the ball as the floor.
            // Resolved after wall clamp so the ball is already inside the viewport.
            bool surfaceHit = ResolveSurfaceCollision(ref pos, ref vel, viewport);
            HasSurfaceContact = surfaceHit;

            entity.Position = pos;
            entity.Velocity = vel;

            // Floor / ceiling impact: transfer horizontal velocity into spin (rolling friction).
            // Use POST-bounce velocity so spin direction matches actual motion direction —
            // critical when the tilted contact normal flips the ball's horizontal velocity.
            if ((wallHit || surfaceHit) && Math.Abs(preBounceVy) > 1)
            {
                double rollOmega = (vel.X / Radius) * (180.0 / Math.PI) * _rollCoupling;
                AngularVelocity = AngularVelocity * (1 - _floorFriction) + rollOmega * _floorFriction;
            }

            // Side-wall impact: vertical velocity translates into spin (climbing/sliding).
            if (wallHit && Math.Abs(vel.X) > 1 && (pos.X <= Radius + 0.5 || pos.X >= viewport.Width - Radius - 0.5))
            {
                double wallOmega = (-vel.Y / Radius) * (180.0 / Math.PI) * _rollCoupling;
                if (pos.X <= Radius + 0.5) wallOmega = -wallOmega;
                AngularVelocity = AngularVelocity * (1 - _floorFriction) + wallOmega * _floorFriction;
            }
        }

        /// <summary>
        /// Treat the bars (and optionally peaks) as a live, dynamic floor surface.
        /// For each column the ball overlaps, find the tallest bar/peak top; the highest
        /// such top across all overlapping columns is the floor for this tick. The bar's
        /// own surface velocity is used for proper relative-velocity collision response
        /// (so a rising bar launches the ball, not just stops it).
        ///
        /// Three physical realism passes layered on the basic depenetration:
        ///  - Velocity-dependent restitution — high-speed impacts lose more energy.
        ///  - Tilted contact normal — when ball straddles columns of different heights, the
        ///    bounce gains a horizontal component (otherwise the ball is stuck on the X axis).
        ///  - Refractory period — a brief cooldown after each bounce suppresses re-bounces
        ///    against the same surface (kills the audio-rate machine-gun effect).
        /// </summary>
        /// <returns>True if the ball was in contact with a surface this tick.</returns>
        private bool ResolveSurfaceCollision(ref Point pos, ref Vector vel, Size viewport)
        {
            if (_bars == null) return false;
            var heights = _bars.BarHeights;
            if (heights.Length == 0 || viewport.Width <= 0) return false;

            double colWidth = viewport.Width / heights.Length;
            if (colWidth <= 0) return false;

            // Columns the ball's AABB overlaps (broad-phase culling)
            int firstCol = (int)Math.Floor((pos.X - Radius) / colWidth);
            int lastCol  = (int)Math.Floor((pos.X + Radius) / colWidth);
            if (firstCol < 0) firstCol = 0;
            if (lastCol  >= heights.Length) lastCol = heights.Length - 1;
            if (firstCol > lastCol) return false;

            // Narrow-phase: for each candidate column, test the ball's actual circular
            // contour against that column's top edge. The ball's bottom-y at horizontal
            // offset dx is  pos.Y + sqrt(R² − dx²)  — only defined for |dx| ≤ R.
            // The deepest-penetrating column is the true contact point; columns that the
            // AABB grazed but the circle doesn't reach are correctly ignored.
            float tallest = 0f;
            float surfaceVy = 0f;
            int contactCol = -1;
            double bestPenetration = 0;

            var peakHeights = _peaks?.PeakHeights;
            var barVelocities = _bars.BarSurfaceVelocities;
            double r2 = Radius * Radius;

            for (int i = firstCol; i <= lastCol; i++)
            {
                float h = heights[i];
                float vy = i < barVelocities.Length ? barVelocities[i] : 0f;
                if (peakHeights != null && i < peakHeights.Length && peakHeights[i] > h)
                {
                    h = peakHeights[i];
                    var peakVelocities = _peaks?.PeakSurfaceVelocities;
                    vy = peakVelocities != null && i < peakVelocities.Length ? peakVelocities[i] : 0f;
                }
                if (h <= 0) continue;

                double colCenterX = (i + 0.5) * colWidth;
                double dx = colCenterX - pos.X;
                double dx2 = dx * dx;
                if (dx2 > r2) continue; // circle doesn't reach this column

                double ballBottomAtCol = pos.Y + Math.Sqrt(r2 - dx2);
                double surfaceTop = viewport.Height - h;
                double penetration = ballBottomAtCol - surfaceTop;
                if (penetration <= 0) continue;

                if (penetration > bestPenetration)
                {
                    bestPenetration = penetration;
                    tallest = h;
                    surfaceVy = vy;
                    contactCol = i;
                }
            }

            if (contactCol < 0) return false; // no actual circular overlap

            // Depenetrate by the exact overlap measured at the contact column.
            pos.Y -= bestPenetration;

            // Relative closing velocity along the (currently vertical) contact normal.
            double vRel = vel.Y - surfaceVy;

            // ---- Refractory period: just depenetrate, do NOT adopt surface velocity ----
            // Forcing the ball to ride a fast-oscillating bar locks it to the surface.
            if (_bounceRefractory > 0)
                return true;

            // ---- Slow contact: ride, no bounce ----
            double cappedSurfaceVy = Math.Clamp(surfaceVy, -SurfaceVelocityCap, SurfaceVelocityCap);

            // Mass-dependent surface coupling: light balls fully adopt the surface velocity;
            // heavy balls resist (a peak can't fling a bowling ball like a beach ball).
            double coupling = SurfaceCouplingRefMass / Math.Max(Mass, SurfaceCouplingRefMass);

            if (vRel <= SurfaceBounceThreshold)
            {
                if (vRel > 0 && Math.Abs(surfaceVy) <= SurfaceVelocityCap)
                    vel.Y = vel.Y + (surfaceVy - vel.Y) * coupling;
                else if (vRel > 0)
                    vel.Y = 0; // surface too jittery to ride; just stop the ball's descent
                return true;
            }

            // ---- Real bounce ----
            // Newton restitution against a driven surface (the surface doesn't recoil,
            // so no two-body mass ratio). The surface velocity is the "moving wall"
            // reference frame; COR determines how much relative velocity is preserved.
            // Surface velocity is attenuated by mass coupling so heavy balls absorb
            // less momentum from the strike.
            double effectiveSurfaceVy = cappedSurfaceVy * coupling;
            double cappedVRel = vel.Y - effectiveSurfaceVy;

            // Velocity-dependent restitution: high-speed impacts deform/heat the ball,
            // losing energy. Scale the loss by (1 - restitution) so high-COR materials
            // (rubber racquetballs) maintain their bounciness at speed, while dead
            // materials (bowling balls, baseballs) lose energy aggressively.
            double materialLoss = RestitutionLossSlope * (1.0 - Restitution);
            double rEff = Restitution * Math.Max(
                MinEffectiveRestitution,
                1.0 - materialLoss * Math.Abs(cappedVRel) / RestitutionLossReference);

            // Two-body bounce: v_ball = v_surface - (1+e) * massRatio * v_rel
            // When massRatio → 1 (surface much heavier), this reduces to Newton restitution.
            // When masses are comparable, the surface shares more of the impulse cost.

            // Tilted contact normal on uneven columns: sample neighbors of the contact column
            // and lean the normal away from the taller side. Lateral kick magnitude is
            // bounded by the BALL's own impact speed (capped) — NOT the bar's velocity —
            // so a frantically wobbling bass column can't whip the ball sideways.
            // Kick is also attenuated by mass coupling so heavy balls resist lateral deflection.
            double tiltX = ComputeNormalTiltX(heights, peakHeights, contactCol);
            if (Math.Abs(tiltX) > 0)
            {
                double impactSpeed = Math.Min(Math.Abs(vel.Y) + Math.Abs(effectiveSurfaceVy), SurfaceVelocityCap);
                vel.X += tiltX * impactSpeed * TiltedNormalKick * coupling;
            }

            // Newton restitution: v_out = v_surface - e_eff * v_rel.
            // The driven surface imparts its (mass-coupled) velocity; COR scales the bounce.
            vel.Y = effectiveSurfaceVy - rEff * cappedVRel;

            // Arm refractory period — prevents oscillating bars from re-bouncing the ball every tick.
            _bounceRefractory = BounceRefractorySeconds;

            return true;
        }

        /// <summary>
        /// Estimate the tangent of the surface tilt at the given column by sampling neighbors
        /// a few columns out (the ball spans many columns, so a 1-column window is too local).
        /// Sign convention: positive return = ball should bounce in +X (right), i.e. the
        /// surface slopes downward to the right or, equivalently, is taller on the LEFT.
        /// Returns 0 when the local slope is below <see cref="NormalTiltThreshold"/>.
        /// </summary>
        private static double ComputeNormalTiltX(float[] heights, float[]? peaks, int col)
        {
            float Sample(int i)
            {
                if (i < 0 || i >= heights.Length) return 0f;
                float h = heights[i];
                if (peaks != null && i < peaks.Length && peaks[i] > h) h = peaks[i];
                return h;
            }

            float left = 0f, right = 0f;
            int leftCount = 0, rightCount = 0;
            for (int k = 1; k <= TiltSampleHalfWidth; k++)
            {
                if (col - k >= 0)             { left  += Sample(col - k); leftCount++; }
                if (col + k < heights.Length) { right += Sample(col + k); rightCount++; }
            }
            if (leftCount == 0 || rightCount == 0) return 0;

            float diff = (left / leftCount) - (right / rightCount); // positive: left is taller
            if (Math.Abs(diff) < NormalTiltThreshold) return 0;

            // Left taller → ball bounces RIGHT (+X). Normalize over a 200px reference range.
            return Math.Clamp(diff / 200.0, -1.0, 1.0);
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Peak
    /// <summary>
    /// Peak-hold physics: each band's marker falls under gravity, holds at impact,
    /// and elastically bounces when bars push back up into it.
    /// Cross-entity coupling: reads live bar heights from a <see cref="Reactivity.Bar"/>.
    /// </summary>
    public sealed class Peak : Physics
    {
        private readonly Reactivity.Bar _bars;
        private float[] _peakHold = [];
        private float[] _peakVelocity = [];
        private int[] _holdTimer = [];
        private float[] _prevPeakHold = [];

        private const int HoldTicks = 30;
        private const float BounceThreshold = 0.5f;

        /// <summary>
        /// Peak heights in pixels per band, readable by the renderer.
        /// </summary>
        public float[] PeakHeights => _peakHold;

        /// <summary>
        /// Per-band screen-space y-velocity of the peak tops (px/sec). Negative = rising.
        /// Computed from frame-to-frame height deltas, same convention as bar surface velocities.
        /// Consumed by ball physics for proper relative-velocity collision response against peaks.
        /// </summary>
        public float[] PeakSurfaceVelocities { get; private set; } = [];

        /// <inheritdoc />
        public override float Gravity => 0.06f;

        /// <inheritdoc />
        public override float Restitution => 0.3f;

        /// <summary>
        /// Create a peak-hold physics component coupled to the given bar reactivity.
        /// </summary>
        public Peak(Reactivity.Bar bars) { _bars = bars; }

        private bool EnsureBuffers()
        {
            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return false;
            if (_peakHold.Length != barHeights.Length)
            {
                _peakHold              = new float[barHeights.Length];
                _peakVelocity          = new float[barHeights.Length];
                _holdTimer             = new int  [barHeights.Length];
                _prevPeakHold          = new float[barHeights.Length];
                PeakSurfaceVelocities  = new float[barHeights.Length];
            }
            return true;
        }

        /// <inheritdoc />
        public override void ApplyForces(World entity, float dt)
        {
            if (!EnsureBuffers()) return;

            // Compute screen-space surface velocities from last tick's height changes.
            // Surface at viewport.Height - peakHeight, so vy = -(peakHeight - prev) / dt.
            // This runs in Phase A before the ball's Phase B collision reads it.
            if (dt > 0)
            {
                for (int i = 0; i < _peakHold.Length; i++)
                {
                    PeakSurfaceVelocities[i] = -(_peakHold[i] - _prevPeakHold[i]) / dt;
                    _prevPeakHold[i] = _peakHold[i];
                }
            }

            for (int i = 0; i < _peakHold.Length; i++)
            {
                if (_holdTimer[i] > 0) _holdTimer[i]--;
                else                    _peakVelocity[i] += Gravity;
            }
        }

        /// <inheritdoc />
        public override void Integrate(World entity, float dt)
        {
            if (_peakHold.Length == 0) return;
            for (int i = 0; i < _peakHold.Length; i++)
            {
                if (_holdTimer[i] > 0) continue;
                _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
            }
        }

        /// <inheritdoc />
        public override void ResolveCollisions(World entity, float dt, Size viewport)
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
    public sealed class Particle : Physics
    {
        private readonly ParticlePool _pool;

        /// <summary>
        /// Optional bar reactivity, queried for live floor heights when resolving rain-drop
        /// collision. Settable post-construction because the pool is created inside Scene
        /// before any BarEntity exists — the WPF host wires this after both are constructed.
        /// </summary>
        public Reactivity.Bar? Bars { get; set; }

        /// <summary>
        /// Optional ball entity reference for drop-vs-ball collision. Drops that hit the
        /// ball splash off in the contact-normal direction (same single-bounce-then-die
        /// rule as bar collision). Settable post-construction.
        /// </summary>
        public World? BallEntityRef { get; set; }

        /// <summary>
        /// Optional peak physics reference for drop-vs-peak collision. When set, rain drops
        /// collide with the higher of bar top or peak marker. Settable post-construction.
        /// </summary>
        public Physics.Peak? PeaksRef { get; set; }

        /// <summary>
        /// Scene-wide wind — the AIR'S velocity (px/sec). Each rain drop computes its drag
        /// against (its velocity − this wind), so smaller (lower-mass) drops accelerate to
        /// match the wind faster than larger drops, producing physically correct varied motion.
        /// Sparks ignore wind entirely; they're impulse-driven and short-lived.
        /// </summary>
        public Vector Wind { get; set; }

        /// <summary>
        /// Linear drag coefficient (units: 1/s). Tuned so a reference (size=1.0) drop
        /// reaches terminal velocity ≈ 350 px/sec under our gravity.
        /// Per-drop drag accel = −(k / size) · v_rel. Linear drag gives Vt ∝ size, producing
        /// a wide ~5:1 speed ratio across the population — small drops drift in the background
        /// while large drops streak past in the foreground, creating natural parallax depth.
        /// </summary>
        private const double DragCoefficient = 1.714;

        /// <summary>
        /// Sample trail history every Nth integration tick. With 120Hz physics and N=3,
        /// a 4-point trail spans ~100ms of motion — enough to look like genuine motion blur
        /// without being so long that quickly-changing wind makes the trail diverge from
        /// the drop's actual trajectory.
        /// </summary>
        private const int TrailSampleInterval = 3;

        /// <summary>Counter for the every-Nth-tick trail sampling.</summary>
        private int _trailSampleCounter;

        /// <inheritdoc />
        public override float Gravity => 600f;

        /// <summary>
        /// Restitution applied when a rain drop bounces off a surface. Drops are mostly
        /// inelastic — they don't behave like balls. Low number, slight retained energy
        /// produces a brief diagonal trail before the drop dies.
        /// </summary>
        public override float Restitution => 0.15f;

        /// <summary>Lifetime (in 120Hz ticks) a rain drop has after using its single bounce.</summary>
        private const int PostBounceLifetime = 30;

        /// <summary>
        /// Create a particle physics component operating on the given pool's buffer.
        /// Pass a bar reactivity to enable rain-drop bouncing off the bar surface;
        /// pass null for spark-only behavior (no surface interaction).
        /// </summary>
        public Particle(ParticlePool pool, Reactivity.Bar? bars = null)
        {
            _pool = pool;
            Bars = bars;
        }

        /// <inheritdoc />
        public override void ApplyForces(World entity, float dt)
        {
            var buffer = _pool.Buffer;
            var wind = Wind;
            double gAccel = Gravity;

            for (int i = 0; i < buffer.Length; i++)
            {
                ref var p = ref buffer[i];
                if (p.FramesLeft <= 0) continue;

                if (p.Kind == ParticlePool.ParticleKind.RainDrop)
                {
                    // Linear drag: accel = −(k / size) · v_rel.
                    // Terminal velocity = g·size/k ∝ size — gives wide parallax-like speed
                    // spread across the population. Small drops drift slowly (distant),
                    // large drops streak fast (close). Wind is the air's velocity — each
                    // drop accelerates toward it at rate k/size, so small drops match wind
                    // faster than large ones.
                    var vRel = p.Velocity - wind;
                    double dragOverMass = DragCoefficient / Math.Max(p.Size, 0.1f);

                    p.Velocity = new Vector(
                        p.Velocity.X - vRel.X * dragOverMass * dt,
                        p.Velocity.Y + gAccel * dt - vRel.Y * dragOverMass * dt);
                }
                else
                {
                    // Sparks: gravity only (preserves original transient/burst behavior).
                    p.Velocity = new Vector(p.Velocity.X, p.Velocity.Y + gAccel * dt);
                }
            }
        }

        /// <inheritdoc />
        public override void Integrate(World entity, float dt)
        {
            var buffer = _pool.Buffer;

            // Trail-history sampling counter: snapshot every Nth tick so a 4-point trail
            // covers a meaningful span of motion rather than 4 nearly-identical positions.
            // At 120Hz physics with sample-every-3 → ~40Hz history → ~25ms between samples,
            // so a full 4-point trail represents ~100ms of trajectory. Looks like genuine
            // motion blur rather than a tiny fragment of the last frame.
            _trailSampleCounter++;
            bool sampleTrail = _trailSampleCounter >= TrailSampleInterval;
            if (sampleTrail) _trailSampleCounter = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                ref var p = ref buffer[i];
                if (p.FramesLeft <= 0) continue;

                // Snapshot pre-integration position into the trail ring (only for rain drops;
                // sparks render as dots and don't need history).
                if (sampleTrail && p.Kind == ParticlePool.ParticleKind.RainDrop)
                {
                    p.Trail3 = p.Trail2;
                    p.Trail2 = p.Trail1;
                    p.Trail1 = p.Trail0;
                    p.Trail0 = p.Position;
                    if (p.TrailLen < 4) p.TrailLen++;
                }

                p.Position += p.Velocity * dt;
            }

            // Lifecycle tick belongs here, not in the pool itself.
            _pool.TickLifetimes();
        }

        /// <inheritdoc />
        public override void ResolveCollisions(World entity, float dt, Size viewport)
        {
            // Spark particles have no surface interaction — they fall and die at lifetime end.
            // Rain drops bounce once off the bars (or the floor, or the ball) and then die quickly.
            if (Bars == null) return;
            var heights = Bars.BarHeights;
            var peakHeights = PeaksRef?.PeakHeights;
            int barCount = heights.Length;
            double colWidth = barCount > 0 ? viewport.Width / barCount : 0;

            // Pre-compute ball collision geometry (cheap), used per drop below.
            var ballPhysics = BallEntityRef?.Physics as Physics.Ball;
            double ballR = ballPhysics?.Radius ?? 0;
            double ballR2 = ballR * ballR;
            bool checkBall = BallEntityRef != null && ballPhysics != null && ballR > 0;
            var ballPos = BallEntityRef?.Position ?? default;

            var buffer = _pool.Buffer;
            for (int i = 0; i < buffer.Length; i++)
            {
                ref var p = ref buffer[i];
                if (p.FramesLeft <= 0) continue;
                if (p.Kind != ParticlePool.ParticleKind.RainDrop) continue;

                // Ball collision first (drops splash off the top of the ball before reaching bars).
                if (checkBall)
                {
                    var d = p.Position - ballPos;
                    double dist2 = d.X * d.X + d.Y * d.Y;
                    if (dist2 <= ballR2)
                    {
                        if (p.BounceUsed) { p.FramesLeft = 0; continue; }

                        double dist = Math.Sqrt(Math.Max(dist2, 0.0001));
                        var normal = d / dist; // unit vector from ball center to drop
                        // Snap drop to the ball's surface
                        p.Position = new Point(ballPos.X + normal.X * ballR, ballPos.Y + normal.Y * ballR);
                        // Reflect velocity through normal: v' = v - 2(v·n)n, scaled by restitution
                        double vDotN = p.Velocity.X * normal.X + p.Velocity.Y * normal.Y;
                        if (vDotN < 0) // only reflect if moving INTO the ball
                        {
                            var reflected = p.Velocity - 2 * vDotN * normal;
                            p.Velocity = reflected * Restitution;
                        }
                        p.BounceUsed = true;
                        if (p.FramesLeft > PostBounceLifetime) p.FramesLeft = PostBounceLifetime;
                        continue; // skip bar check for this drop
                    }
                }

                // Surface y at this drop's column (top of the tallest thing under it).
                double surfaceY = viewport.Height; // floor as fallback
                if (barCount > 0 && colWidth > 0 && p.Position.X >= 0 && p.Position.X < viewport.Width)
                {
                    int col = Math.Clamp((int)(p.Position.X / colWidth), 0, barCount - 1);
                    double barTop = viewport.Height - heights[col];
                    if (barTop < surfaceY) surfaceY = barTop;

                    // Peak markers sit above bars — drops should collide with them too
                    if (peakHeights != null && col < peakHeights.Length)
                    {
                        double peakTop = viewport.Height - peakHeights[col];
                        if (peakTop < surfaceY) surfaceY = peakTop;
                    }
                }

                // Swept collision: check if the drop crossed the surface during this
                // tick, not just whether it's currently below. Prevents tunneling at
                // high velocity (large drops can move ~5 px/tick at 120Hz).
                double prevY = p.TrailLen >= 1 ? p.Trail0.Y : p.Position.Y - p.Velocity.Y * dt;
                bool crossed = prevY < surfaceY && p.Position.Y >= surfaceY;
                bool below = p.Position.Y >= surfaceY;

                if (!crossed && !below) continue; // not yet contacting

                if (p.BounceUsed)
                {
                    // Already bounced once — this is a re-contact, kill the drop now.
                    p.FramesLeft = 0;
                    continue;
                }

                // Single inelastic bounce: snap to surface, retain a fraction of vertical
                // velocity (now upward) and most of horizontal velocity.
                p.Position = new Point(p.Position.X, surfaceY);
                p.Velocity = new Vector(p.Velocity.X * 0.6, -Math.Abs(p.Velocity.Y) * Restitution);
                p.BounceUsed = true;

                // Reset trail to start from the impact point — otherwise the streak would
                // visibly cross through the bar surface in a straight line.
                p.TrailLen = 0;
                p.Trail0 = p.Position;
                p.Trail1 = p.Position;
                p.Trail2 = p.Position;
                p.Trail3 = p.Position;

                // Shrink remaining lifetime so the post-bounce trail fades out fast —
                // the drop has "splashed" and is just briefly visible afterward.
                if (p.FramesLeft > PostBounceLifetime) p.FramesLeft = PostBounceLifetime;
            }
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Goal
    /// <summary>
    /// Goal-zone physics: detects when a ball entity enters the goal radius and fires
    /// the <see cref="World.Collision"/> event to signal a stage clear.
    /// No forces or integration — the goal is static.
    /// </summary>
    public sealed class Goal : Physics
    {
        private readonly double _radius;
        private readonly World _ball;
        private bool _triggered;

        /// <summary>
        /// When false, the goal is suppressed — no collision detection occurs.
        /// Used by game-mode anti-cheat to hide the goal while the user drags the ball.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Optional liveness gate, wired by <see cref="Entities.Goal"/> to the steering
        /// component's charge state. When set and returns false the trigger is suppressed
        /// even though the component is Enabled — this is what gives a fresh-spawned goal
        /// a charge-up grace period so it doesn't instantly score on an overlapping ball.
        /// Null ⇒ no gate (legacy / standalone use always live).
        /// </summary>
        public Func<bool>? IsLive { get; set; }

        public Goal(double radius, World ball)
        {
            _radius = radius;
            _ball = ball;
        }

        public override void ResolveCollisions(World entity, float dt, Size viewport)
        {
            if (!Enabled || _triggered || _ball == null || !_ball.IsAlive) return;
            if (IsLive is { } gate && !gate()) return;

            var diff = _ball.Position - entity.Position;

            // Trigger when ball center enters the goal circle
            if (diff.Length < _radius)
            {
                _triggered = true;
                entity.NotifyCollision(new CollisionInfo(entity.Position, new Vector(0, -1), 1.0f));
            }
        }
    }
    #endregion
}
