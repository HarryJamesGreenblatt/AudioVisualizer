using System;
using System.Windows;

namespace WavBall.Components;

/// <summary>
/// Abstract base for autonomous-agent motion. Where <see cref="Physics"/> moves entities
/// in response to external forces, <see cref="Steering"/> moves them according to their
/// own intent — pick a target, decide where to go, head there.
///
/// Reynolds-style steering primitives (seek, arrive, wander) are the natural building
/// blocks, but each concrete behavior composes them as it sees fit. The pipeline runs
/// between <see cref="Reactivity"/> (which produces signals the agent senses) and
/// <see cref="Physics"/> (which would still own collision / forced motion if any).
///
/// Concrete behaviors are nested types so the autonomous-agent surface lives in one
/// file. The first inhabitant is <see cref="Goal"/> — a "moth-to-flame" fairy that
/// hovers above whichever frequency band is most musically active. Future agents
/// (enemies, decorative spirits, NPCs) would slot in alongside it.
/// </summary>
public abstract class Steering
{
    #region Properties
    /// <summary>
    /// When false, the agent is suspended — <see cref="Steer"/> becomes a no-op.
    /// Used for anti-cheat / pause / state-machine gating without removing the component.
    /// </summary>
    public bool Enabled { get; set; } = true;
    #endregion

    #region Pipeline
    /// <summary>
    /// Advance the agent for one fixed-timestep tick. Override to read signals from the
    /// owning entity's other components and mutate <see cref="World.Position"/> (and any
    /// internal velocity state) accordingly. Default no-op so subclasses opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling and bounds.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    public virtual void Steer(World entity, Size viewport, float dt) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Goal
    /// <summary>
    /// "Moth-to-flame" steering for the Goal entity, organized as a <b>Nystrom-style
    /// state machine</b> (Game Programming Patterns, ch. State). The shared steering
    /// pipeline — audio-driven wander, seek-with-arrival, integration, viewport clamp —
    /// is invariant; only the choice of <i>which band to target this tick</i> varies.
    /// Each <see cref="Mood"/> owns:
    /// <list type="bullet">
    ///   <item><description>its band-selection rule (e.g. max amplitude, max heat, min activity)</description></item>
    ///   <item><description>its dwell duration (uniform in [MinDwell, MaxDwell])</description></item>
    ///   <item><description>its choice of successor when the dwell expires</description></item>
    /// </list>
    /// This routes the fairy's "intent" without a giant central switch — the same way
    /// Nystrom's animation/character states avoid nested conditionals when behaviors
    /// compose (jumping while ducking while swinging). New moods are pure additions:
    /// drop a class into <see cref="MoodPool"/> and it joins the rotation.
    ///
    /// <para>Per-tick flow:</para>
    /// <list type="number">
    ///   <item><description>Tick mood timer; on expiry, ask the mood for its successor.</description></item>
    ///   <item><description>Mood picks a band (or returns -1 → fall back to spawn).</description></item>
    ///   <item><description>Audio-driven wander offset is added to the target.</description></item>
    ///   <item><description>Seek-with-arrival produces desired velocity → steering force.</description></item>
    ///   <item><description>Integrate, damp, cap, clamp to viewport.</description></item>
    /// </list>
    /// </summary>
    public sealed class Goal : Steering
    {
        // ── References & spawn ──
        private readonly double _radius;
        private readonly Point _spawn;
        private readonly Reactivity.Bar _bars;

        // ── Agent state ──
        private double _vx, _vy;
        private double _wanderAngle;

        // ── Mood state machine ──
        private Mood _mood = null!;          // set in ctor via SetMood
        private double _moodTimer;
        private double _moodDuration;
        private readonly Random _rng = new();

        // ── Charge state ──
        /// <summary>
        /// Personal "engagement" accumulator, 0–1. Grows when the active mood picks a
        /// strong band (musical engagement) and leaks slowly otherwise. Drives both the
        /// glow envelope in <see cref="Rendering.Goal"/> and the trigger gate in
        /// <see cref="Physics.Goal"/> — so a fresh-spawned goal is visibly depleted AND
        /// physically inert, then both light up together as it locks onto the music.
        /// </summary>
        private double _charge;

        // ── Tunables ──

        /// <summary>Top speed (px/sec). Caps both desired velocity in seek and the integrated velocity.</summary>
        private const double MaxSpeed = 280.0;

        /// <summary>Cap on steering acceleration magnitude (px/sec²). Limits how quickly the agent can change direction — gives motion a "with mass" feel rather than instant snapping.</summary>
        private const double MaxForce = 900.0;

        /// <summary>Arrival radius (px). Inside this distance from the target, desired speed scales linearly to zero — prevents overshoot, gives the moth its deceleration near the flame.</summary>
        private const double ArriveRadius = 90.0;

        /// <summary>Minimum bar height (px) for a band to be considered "active" by amplitude-based moods. Bands below this are treated as silent / unfocused.</summary>
        private const double ActiveBandFloor = 8.0;

        /// <summary>Minimum band-heat for a band to be considered "warm" by heat-based moods.</summary>
        private const double WarmBandFloor = 0.05;

        /// <summary>Pixels above the target bar's top edge to aim for. The fairy hovers ABOVE the flame, not inside it.</summary>
        private const double HoverOffset = 35.0;

        /// <summary>Orbital wander amplitude (px). Target is jittered by (cos, sin) of the wander angle so the fairy describes a loose orbit rather than locking on.</summary>
        private const double WanderRadius = 28.0;

        /// <summary>Wander angle drift rate (rad/sec). Higher = jitterier orbit; lower = smoother loops.</summary>
        private const double WanderJitter = 2.5;

        /// <summary>Ambient damping (1/sec) applied as exponential velocity decay every tick. Bleeds energy continuously without divergence.</summary>
        private const double AmbientDamp = 1.8;

        /// <summary>Maximum lateral drift from spawn X, as fraction of viewport width.</summary>
        private const double DriftMaxXFrac = 0.50;

        /// <summary>Maximum vertical drift from spawn Y, as fraction of viewport height.</summary>
        private const double DriftMaxYFrac = 0.40;

        // ── Charge dynamics: capacitor charging from the spectrum's scalar
        //    potential at the goal's current position. Treats the bars as a continuous
        //    charge distribution (each band a softened point charge at its hover point)
        //    rather than a single target. The goal absorbs from whatever portion of the
        //    distribution it's currently sitting in — so wandering near a hot region
        //    charges it even without exact alignment, and drifting into a quiet region
        //    discharges it. Decoupled from the steering: the moods still pick a single
        //    target for motion, but the charge sees the whole field.

        /// <summary>Softening length (px) in the 1/r kernel. Prevents divergence when the goal sits exactly on a band's hover point; sets the "close-range plateau" at which a single band's contribution saturates.</summary>
        private const double SoftEps = 50.0;

        /// <summary>Reference potential at which the capacitor's target reaches 1.0. Tuned so that hovering over a moderately loud part of the spectrum drives charge to saturation.</summary>
        private const double VRef = 25.0;

        /// <summary>Capacitor time constant (seconds). Same value governs both charge and discharge — symmetric breathing rather than asymmetric gain/leak.</summary>
        private const double CapTau = 1.2;

        /// <summary>Charge level at or above which the goal is "armed" — Physics.Goal will fire collisions, Rendering.Goal reaches full glow envelope.</summary>
        private const double ArmThreshold = 0.40;

        public Goal(double radius, Point spawn, Reactivity.Bar bars)
        {
            _radius = radius;
            _spawn = spawn;
            _bars = bars;
            SetMood(MoodPool[0]);  // start in HuntPeak
        }

        /// <summary>Name of the currently active mood — useful for diagnostic overlays.</summary>
        public string CurrentMood => _mood.Name;

        /// <summary>Current charge level, 0–1. Read by Rendering.Goal as the glow envelope.</summary>
        public double Charge => _charge;

        /// <summary>True once charge has reached <see cref="ArmThreshold"/>. Read by Physics.Goal to gate the trigger so a fresh-spawned goal doesn't instantly score on an overlapping ball.</summary>
        public bool IsArmed => _charge >= ArmThreshold;

        /// <inheritdoc />
        public override void Steer(World entity, Size viewport, float dt)
        {
            if (!Enabled || dt <= 0) return;
            double w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return;

            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return;

            // ── 1. Tick mood; transition on dwell expiry ──
            _moodTimer += dt;
            if (_moodTimer >= _moodDuration)
            {
                SetMood(_mood.OnDwellExpire(this));
            }

            // ── 2. Mood picks a band (-1 ⇒ no qualifying band → drift to spawn) ──
            int band = _mood.PickBand(this);
            double colWidth = w / barHeights.Length;
            Point target = band >= 0
                ? new Point((band + 0.5) * colWidth, h - barHeights[band] - HoverOffset)
                : _spawn;

            // ── 2a. Charge from distributed-field potential (capacitor model) ──
            // Sample the scalar potential V = Σ q_i / sqrt(r_i² + ε²) of the whole
            // spectrum at the goal's current position. Each band is a softened point
            // charge at its hover point with magnitude q_i = barHeights[i]. Capacitor
            // dynamics dQ/dt = (V_norm − Q)/τ mean charge rises when the goal sits in
            // a high-potential region (near loud bands) and falls otherwise — same
            // time constant in both directions, so charge breathes with the music
            // rather than ratcheting up. Decoupled from steering: motion still seeks a
            // single target, but the charge sees the entire distribution.
            double V = 0;
            double eps2 = SoftEps * SoftEps;
            double gx = entity.Position.X;
            double gy = entity.Position.Y;
            for (int i = 0; i < barHeights.Length; i++)
            {
                double q = barHeights[i];
                if (q <= 0) continue;
                double bx = (i + 0.5) * colWidth;
                double by = h - barHeights[i] - HoverOffset;
                double ddx = bx - gx;
                double ddy = by - gy;
                double r = Math.Sqrt(ddx * ddx + ddy * ddy + eps2);
                V += q / r;
            }
            double vNorm = Math.Clamp(V / VRef, 0, 1);
            _charge += (vNorm - _charge) * (dt / CapTau);
            _charge = Math.Clamp(_charge, 0, 1);

            // ── 3. Audio-driven wander, shared across all moods ──
            // Quiet → slow drift; busy → fast orbit; snare hits → angle twitches.
            double wanderRate = (0.4 + _bars.Energy * 3.0) * WanderJitter;
            _wanderAngle += wanderRate * dt + _bars.SnareFlux * 1.2;
            target.X += Math.Cos(_wanderAngle) * WanderRadius;
            target.Y += Math.Sin(_wanderAngle) * WanderRadius * 0.5;

            // ── 4. Seek with arrival ──
            double goalX = entity.Position.X;
            double goalY = entity.Position.Y;
            double dx = target.X - goalX;
            double dy = target.Y - goalY;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            double desiredVx = 0, desiredVy = 0;
            if (dist > 0.1)
            {
                double desiredSpeed = (dist > ArriveRadius)
                    ? MaxSpeed
                    : MaxSpeed * (dist / ArriveRadius);
                desiredVx = (dx / dist) * desiredSpeed;
                desiredVy = (dy / dist) * desiredSpeed;
            }

            double steerX = desiredVx - _vx;
            double steerY = desiredVy - _vy;

            // Cap steering magnitude so direction changes feel weighted, not snapping.
            double steerMag = Math.Sqrt(steerX * steerX + steerY * steerY);
            if (steerMag > MaxForce)
            {
                double k = MaxForce / steerMag;
                steerX *= k;
                steerY *= k;
            }

            // ── 5. Integrate velocity, apply damping, cap speed, integrate position ──
            _vx += steerX * dt;
            _vy += steerY * dt;

            double dampFactor = Math.Exp(-AmbientDamp * dt);
            _vx *= dampFactor;
            _vy *= dampFactor;

            double sp = Math.Sqrt(_vx * _vx + _vy * _vy);
            if (sp > MaxSpeed)
            {
                double k = MaxSpeed / sp;
                _vx *= k;
                _vy *= k;
            }

            double newX = goalX + _vx * dt;
            double newY = goalY + _vy * dt;

            // Hard lateral bounds — kill velocity on impact so the agent doesn't pin.
            double minX = _spawn.X - w * DriftMaxXFrac;
            double maxX = _spawn.X + w * DriftMaxXFrac;
            if (newX < minX) { newX = minX; if (_vx < 0) _vx = 0; }
            if (newX > maxX) { newX = maxX; if (_vx > 0) _vx = 0; }

            // Hard vertical bounds.
            double minY = _spawn.Y - h * DriftMaxYFrac;
            double maxY = _spawn.Y + h * DriftMaxYFrac;
            if (newY < minY) { newY = minY; if (_vy < 0) _vy = 0; }
            if (newY > maxY) { newY = maxY; if (_vy > 0) _vy = 0; }

            // NOTE: no bar/peak exclusion. The goal is a moth, not a ball — it passes
            // through the spectrum freely. Clamping against bar tops caused upper-corner
            // pinning when tall bars in a clipped X range stacked the Y clamp against
            // the lateral bound; the natural seek-with-arrival keeps the fairy near
            // (but not stuck on) the active bands without any collision response.

            entity.Position = new Point(newX, newY);
        }

        // ── Mood machine ────────────────────────────────────────────────────

        /// <summary>Adopt a mood and randomize its dwell within [MinDwell, MaxDwell].</summary>
        private void SetMood(Mood next)
        {
            _mood = next;
            _moodTimer = 0;
            _moodDuration = next.MinDwell + _rng.NextDouble() * (next.MaxDwell - next.MinDwell);
        }

        /// <summary>Pick any mood other than <paramref name="current"/> uniformly at random.</summary>
        private Mood RandomMoodExcept(Mood current)
        {
            // Rejection sample so consecutive moods always differ — keeps the rotation legible.
            while (true)
            {
                var pick = MoodPool[_rng.Next(MoodPool.Length)];
                if (pick != current) return pick;
            }
        }

        /// <summary>
        /// Stateless mood instances shared across all <see cref="Goal"/> agents — each
        /// mood reads/writes the host's blackboard rather than carrying state, so a
        /// single instance per type is sufficient. To add a new mood: define a nested
        /// subclass and add it to this array.
        /// </summary>
        private static readonly Mood[] MoodPool =
        {
            new HuntPeak(),
            new HuntHeat(),
            new SeekQuiet(),
        };

        /// <summary>
        /// Base mood. Each subclass picks a target band by its own rule; the shared
        /// steering pipeline handles wander, seek, integration, and clamping. Following
        /// Nystrom's State Pattern, the mood also owns its dwell timing and its choice
        /// of successor — no central switch decides "what comes next".
        /// </summary>
        private abstract class Mood
        {
            public abstract string Name { get; }
            /// <summary>Minimum seconds the mood holds before considering a transition.</summary>
            public abstract double MinDwell { get; }
            /// <summary>Maximum seconds the mood holds; actual dwell is uniform in [Min, Max].</summary>
            public abstract double MaxDwell { get; }

            /// <summary>
            /// Resolve a target band index per this mood's preference. Return -1 if no
            /// band qualifies — the steering pipeline then drifts toward the spawn point.
            /// </summary>
            public abstract int PickBand(Goal host);

            /// <summary>
            /// Decide the next mood when this one's dwell expires. Default: pick any
            /// other mood uniformly at random. Override for state-specific transitions
            /// (e.g. always-follow chains, conditional yields on silence, etc.).
            /// </summary>
            public virtual Mood OnDwellExpire(Goal host) => host.RandomMoodExcept(this);
        }

        /// <summary>
        /// Chase whichever band is LOUDEST right now (max amplitude). Naturally
        /// bass-biased because low frequencies dominate amplitude — the fairy
        /// buzzes around the kick / sub.
        /// </summary>
        private sealed class HuntPeak : Mood
        {
            public override string Name => "HuntPeak";
            public override double MinDwell => 2.5;
            public override double MaxDwell => 5.0;
            public override int PickBand(Goal host)
            {
                var bars = host._bars.BarHeights;
                int best = -1; double bestH = ActiveBandFloor;
                for (int i = 0; i < bars.Length; i++)
                {
                    if (bars[i] > bestH) { bestH = bars[i]; best = i; }
                }
                return best;
            }
        }

        /// <summary>
        /// Chase whichever band has the highest accumulated HEAT (smoothed,
        /// sensitivity-corrected). Picks bands that have been "interesting" for a
        /// while — drifts to where the musical pattern lives rather than where the
        /// instantaneous peak is.
        /// </summary>
        private sealed class HuntHeat : Mood
        {
            public override string Name => "HuntHeat";
            public override double MinDwell => 3.0;
            public override double MaxDwell => 6.0;
            public override int PickBand(Goal host)
            {
                var heat = host._bars.BandHeat;
                int best = -1; double bestH = WarmBandFloor;
                for (int i = 0; i < heat.Length; i++)
                {
                    if (heat[i] > bestH) { bestH = heat[i]; best = i; }
                }
                return best;
            }
        }

        /// <summary>
        /// Seek the QUIETEST non-silent band — the most active band that's still dim.
        /// Because bass dominates amplitude, this naturally biases right toward the
        /// treble tail, giving the fairy a reason to explore the screen's right side
        /// instead of perpetually hugging the left.
        /// </summary>
        private sealed class SeekQuiet : Mood
        {
            public override string Name => "SeekQuiet";
            public override double MinDwell => 2.0;
            public override double MaxDwell => 4.0;
            public override int PickBand(Goal host)
            {
                var bars = host._bars.BarHeights;
                int best = -1; double bestH = double.MaxValue;
                for (int i = 0; i < bars.Length; i++)
                {
                    // Require minimum signal (skip dead bands), then take the smallest.
                    if (bars[i] > ActiveBandFloor && bars[i] < bestH)
                    {
                        bestH = bars[i];
                        best = i;
                    }
                }
                return best;
            }
        }
    }
    #endregion
}
