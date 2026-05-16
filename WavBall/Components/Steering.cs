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
    /// "Mosquito" steering for the Goal entity, organized as a <b>two-dimensional
    /// appetite machine</b> (Nystrom-style State Pattern). The shared steering pipeline —
    /// audio-driven wander, seek-with-arrival, integration, viewport clamp — is
    /// invariant; only the choice of <i>which band to target this tick</i> varies with mood.
    ///
    /// <para>Two orthogonal signals drive the cycle, modelling the mosquito's environment
    /// and metabolism separately:</para>
    /// <list type="bullet">
    ///   <item><description><b>Charge</b> (sensor, supplied by <see cref="Charge.Goal"/> via <see cref="ChargeSource"/>): "am I on food <i>right now?</i>" — a fast Gaussian field reading at the goal's current position. Read-only here.</description></item>
    ///   <item><description><b>Satiety</b> (metabolic state, owned by this component): "am I currently <i>full?</i>" — a slow 0–1 accumulator that ticks UP while Feeding (proportional to charge: you only fill when actually feeding) and DOWN while Sated (proportional to <c>1−charge</c>: you digest faster in genuinely cold spots).</description></item>
    /// </list>
    /// <para>Why two dimensions instead of one. Charge alone can't drive both phases
    /// reliably: the bass-dominated loudest band keeps shifting, so charge plateaus
    /// at some mid value and rarely crosses a fixed Schmitt threshold; even when it
    /// does, the transit time to the anti-centroid is long enough that charge drops
    /// below the hungry threshold mid-flight, flipping the mood back before the goal
    /// ever <i>arrives</i> at the cold spot. Satiety is integrated over time, so it
    /// fills as long as the goal finds <i>any</i> heat at <i>any</i> moment, and it
    /// only drains noticeably once the goal has genuinely left the loud region —
    /// guaranteeing the cycle completes regardless of how flickery the spectrum is.</para>
    /// <para>Two guards keep the cycle well-behaved:
    /// <see cref="MinDwell"/> prevents mood-flicker at the boundary, and
    /// <see cref="MaxDwell"/> forces a flip after a hard timeout so degenerate audio
    /// (silence, pure-DC, etc.) doesn't pin the mood indefinitely.</para>
    /// <list type="bullet">
    ///   <item><description><see cref="Feeding"/> — hungry; targets the band that wins a proximity-weighted loudness contest (nearby modest band can beat far loud one). From the center this still picks the bass; from a far-side position the goal feeds locally rather than snapping back. Transitions to <see cref="Sated"/> when satiety saturates (or MaxDwell elapses).</description></item>
    ///   <item><description><see cref="Sated"/> — full; targets the spectrum's <i>anti-centroid</i> (the band index mirroring the spectral centroid) so the goal actively pulls AWAY from wherever loudness is concentrated, finding a cold spot to bleed off. Transitions back to <see cref="Feeding"/> when satiety empties (or MaxDwell elapses).</description></item>
    /// </list>
    /// New moods are pure additions — define a nested subclass with its
    /// <see cref="Mood.PickBand"/> rule, <see cref="Mood.Tick"/> transition logic,
    /// and (optionally) <see cref="Mood.InitialSatiety"/> entry value.
    ///
    /// <para>Per-tick flow:</para>
    /// <list type="number">
    ///   <item><description>Tick dwell timer; drift satiety (Feeding fills with charge, Sated drains with 1−charge).</description></item>
    ///   <item><description>Mood self-evaluates against satiety + dwell; may return a successor.</description></item>
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

        /// <summary>
        /// Fractional band index corresponding to the goal's current X position.
        /// Updated once per tick at the top of <see cref="Steer"/> so mood
        /// <see cref="Mood.PickBand"/> implementations can apply proximity weighting
        /// without needing the entity / viewport in their signature.
        /// </summary>
        private double _currentBandIdx;

        // ── Appetite machine ( Charge sensor + Satiety state ) ──
        private Mood _mood = null!;          // set in ctor via SetMood
        /// <summary>Metabolic accumulator, 0–1. 0 = empty/hungry, 1 = full/sated. Drifts up while Feeding (proportional to charge), down while Sated (proportional to 1−charge). Reset by <see cref="SetMood"/> on each transition.</summary>
        private double _satiety;
        /// <summary>Seconds elapsed since the current mood was entered. Used with <see cref="MinDwell"/>/<see cref="MaxDwell"/> to debounce/guarantee transitions.</summary>
        private double _moodTime;

        /// <summary>
        /// Lambda readback to the sibling <see cref="Charge.Goal"/> component's value.
        /// Wired by the owning entity so this component stays decoupled from the
        /// concrete charge implementation — it just asks "what's my engagement right
        /// now?" and decides which mood to be in. Null → treated as 0 (always hungry).
        /// </summary>
        public Func<double>? ChargeSource { get; set; }

        // ── Tunables ──

        /// <summary>Top speed (px/sec). Caps both desired velocity in seek and the integrated velocity.</summary>
        private const double MaxSpeed = 280.0;

        /// <summary>Cap on steering acceleration magnitude (px/sec²). Limits how quickly the agent can change direction — gives motion a "with mass" feel rather than instant snapping.</summary>
        private const double MaxForce = 900.0;

        /// <summary>Arrival radius (px). Inside this distance from the target, desired speed scales linearly to zero — prevents overshoot, gives the moth its deceleration near the flame.</summary>
        private const double ArriveRadius = 90.0;

        /// <summary>Minimum bar height (px) for a band to be considered "active" by amplitude-based moods. Bands below this are treated as silent / unfocused.</summary>
        private const double ActiveBandFloor = 8.0;

        /// <summary>Standard deviation of the proximity weight used by <see cref="Feeding"/>, expressed as a fraction of the band count. The Feeding mood scores each band by <c>height · exp(−Δi² / 2σ²)</c>, so this controls how strongly the mosquito prefers a nearby modest band over a far loud one. Smaller → more local (mosquito hovers around its current X); larger → more global (still likely to snap back to bass). 0.30 keeps bass winning from the center of the screen but lets a far-side goal stay and feed on modest local heat instead of pendulum-ing back to the bass every cycle.</summary>
        private const double ProximitySigmaFrac = 0.30;

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

        // ── Appetite dynamics (Satiety drift + dwell guards) ──

        /// <summary>Maximum fill rate (units/sec) for <see cref="_satiety"/> while Feeding, scaled by current charge each tick. At full charge (1.0) satiety fills to 1.0 in ~1/rate seconds; at average charge ~0.5 the effective time-to-full is ~4 seconds.</summary>
        private const double SatietyFillRate = 0.55;

        /// <summary>Maximum drain rate (units/sec) for <see cref="_satiety"/> while Sated, scaled by (1−charge) each tick. Genuinely cold spots (charge≈0) drain at the full rate; transit through moderately-loud regions drains slower, so the goal has time to reach the anti-centroid before flipping back.</summary>
        private const double SatietyDrainRate = 0.20;

        /// <summary>Minimum seconds the goal must remain in a mood before any transition. Debounces the boundary so a brief satiety dip/spike can't flicker the mood.</summary>
        private const double MinDwell = 1.5;

        /// <summary>Hard maximum seconds in a single mood. Forces a flip even if satiety hasn't reached its limit — guarantees the cycle progresses through degenerate audio (silence, pure-DC, uniformly loud) where satiety can't be driven all the way to its bound.</summary>
        private const double MaxDwell = 14.0;

        public Goal(double radius, Point spawn, Reactivity.Bar bars)
        {
            _radius = radius;
            _spawn = spawn;
            _bars = bars;
            SetMood(FeedingMood);  // start hungry
        }

        /// <summary>Name of the currently active mood — useful for diagnostic overlays.</summary>
        public string CurrentMood => _mood.Name;

        /// <summary>Current satiety (metabolic fullness), 0–1. Read for diagnostic overlays.</summary>
        public double Satiety => _satiety;

        /// <summary>Seconds elapsed in the current mood. Read for diagnostic overlays.</summary>
        public double MoodTime => _moodTime;

        /// <inheritdoc />
        public override void Steer(World entity, Size viewport, float dt)
        {
            if (!Enabled || dt <= 0) return;
            double w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return;

            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return;

            // Stash the goal's current X as a fractional band index so moods can
            // apply proximity weighting without taking entity / viewport in their
            // signature. Computed once per tick before any mood logic runs.
            double colWidth = w / barHeights.Length;
            _currentBandIdx = Math.Clamp(entity.Position.X / colWidth - 0.5, 0, barHeights.Length - 1);

            // ── 1. Drift satiety from charge, then let the mood self-evaluate ──
            // Charge (sensor): am I on food right now?  Satiety (state): am I full?
            // Feeding fills satiety proportional to charge — you only feel full when
            // you actually feed. Sated drains it proportional to (1−charge) — you
            // digest faster in cold spots, slower in transit through loud regions.
            // This decouples the cycle from any single Schmitt threshold on a noisy
            // signal: even if charge oscillates, satiety integrates and reaches its
            // bound monotonically. MinDwell debounces; MaxDwell guarantees cycling.
            _moodTime += dt;
            double chargeNow = Math.Clamp(ChargeSource?.Invoke() ?? 0.0, 0, 1);
            if (_mood is Feeding)
                _satiety += chargeNow * SatietyFillRate * dt;
            else // Sated
                _satiety -= (1.0 - chargeNow) * SatietyDrainRate * dt;
            _satiety = Math.Clamp(_satiety, 0, 1);

            var next = _mood.Tick(this);
            if (!ReferenceEquals(next, _mood)) SetMood(next);

            // ── 2. Mood picks a band (-1 ⇒ no qualifying band → drift to spawn) ──
            // X comes from the band index; Y is mood-owned so Feeding can hover above
            // the bar top while Sated can retreat to the top of the viewport — out of
            // the audio potential field entirely, where discharge is plausible.
            int band = _mood.PickBand(this);
            Point target = band >= 0
                ? new Point((band + 0.5) * colWidth, _mood.ResolveTargetY(this, band, h, barHeights))
                : _spawn;

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

        // ── Appetite machine ────────────────────────────────────────────────

        /// <summary>
        /// Per-mood scratch slot — currently used by <see cref="Sated"/> to randomize
        /// its retreat altitude on each entry (set in <see cref="Mood.OnEnter"/>,
        /// read in <see cref="Mood.ResolveTargetY"/>). Kept on the host rather than
        /// on the mood instance because moods are shared singletons.
        /// </summary>
        private double _satedRetreatYFrac;

        /// <summary>Adopt a mood. Resets the satiety accumulator to the mood's <see cref="Mood.InitialSatiety"/> (e.g. entering Sated starts at 1.0 because you just got full; entering Feeding starts at 0.0 because you just got empty) and zeros the dwell timer. Calls <see cref="Mood.OnEnter"/> so moods can seed any per-entry random state.</summary>
        private void SetMood(Mood next)
        {
            _mood = next;
            _moodTime = 0;
            _satiety = next.InitialSatiety;
            next.OnEnter(this);
        }

        /// <summary>
        /// Stateless mood singletons — each mood reads the host's blackboard
        /// (charge, bars, position) rather than carrying state, so one instance per
        /// type is sufficient and the appetite cycle's structure is reified as the
        /// transitions in each mood's <see cref="Mood.Tick"/>.
        /// </summary>
        private static readonly Feeding FeedingMood = new();
        private static readonly Sated   SatedMood   = new();

        /// <summary>
        /// Base mood. Each subclass picks a target band by its own rule AND owns its
        /// own transition logic via <see cref="Tick"/> — the appetite cycle's edges
        /// are reified as the mood's self-evaluation, no central switch decides
        /// "what comes next".
        /// </summary>
        private abstract class Mood
        {
            public abstract string Name { get; }

            /// <summary>
            /// Resolve a target band index per this mood's preference. Return -1 if no
            /// band qualifies — the steering pipeline then drifts toward the spawn point.
            /// </summary>
            public abstract int PickBand(Goal host);

            /// <summary>
            /// Called every tick. Return <c>this</c> to remain in the current mood, or
            /// another mood instance to transition. Implementations typically gate on
            /// the host's <see cref="Satiety"/> reaching a bound plus <see cref="MoodTime"/>
            /// passing <see cref="MinDwell"/>, with a <see cref="MaxDwell"/> override so
            /// the cycle still progresses under degenerate audio.
            /// </summary>
            public virtual Mood Tick(Goal host) => this;

            /// <summary>
            /// Satiety value the host resets to when this mood is entered. Default 0
            /// ("freshly empty" — appropriate for hungry-style moods like Feeding).
            /// Override to 1.0 for full-style moods like Sated, or any in-between
            /// value for a mood that starts partially saturated.
            /// </summary>
            public virtual double InitialSatiety => 0.0;

            /// <summary>
            /// Resolve the world-space Y for the picked band. Default hovers
            /// <see cref="HoverOffset"/> above the bar's top edge — appropriate for
            /// feeding moods that want to sit on the flame. Override for moods that
            /// want to escape the audio potential field entirely (e.g. <see cref="Sated"/>
            /// retreats toward the top of the viewport so the Gaussian field of the
            /// underlying band can't keep recharging the sensor).
            /// </summary>
            public virtual double ResolveTargetY(Goal host, int band, double viewportH, float[] barHeights)
                => viewportH - barHeights[band] - HoverOffset;

            /// <summary>
            /// Hook called by <see cref="SetMood"/> when this mood is entered. Default
            /// no-op. Override to seed per-entry random state on the host (e.g.
            /// <see cref="Sated"/> picks a fresh retreat altitude here so each Sated
            /// cycle looks different rather than always parking in the same corner).
            /// </summary>
            public virtual void OnEnter(Goal host) { }
        }

        /// <summary>
        /// HUNGRY: chase whichever band wins a <b>proximity-weighted loudness contest</b>:
        /// <c>score(i) = bars[i] · exp(−Δi² / 2σ²)</c> where Δi is band-distance from the
        /// goal's current X and σ = <see cref="ProximitySigmaFrac"/> · bandCount. From the
        /// center of the screen this still elects the bass (dominant amplitude wins the
        /// product even at moderate distance penalty), but from a far-side position the
        /// mosquito will prefer a modest-loudness band right next to it rather than
        /// pendulum-ing back across the screen to the bass every cycle. Combined with the
        /// Sated anti-centroid trip this lets the goal <i>migrate</i> across the spectrum
        /// over time instead of orbiting a single energy peak. Satiety fills as the goal
        /// feeds; once it saturates (or MaxDwell elapses) we flip to <see cref="Sated"/>.
        /// Entry satiety is the default 0 (just got empty).
        /// </summary>
        private sealed class Feeding : Mood
        {
            public override string Name => "Feeding";
            public override int PickBand(Goal host)
            {
                var bars = host._bars.BarHeights;
                if (bars.Length == 0) return -1;

                double sigma = bars.Length * ProximitySigmaFrac;
                double twoSigmaSq = 2.0 * sigma * sigma;
                double currentIdx = host._currentBandIdx;

                int best = -1;
                double bestScore = 0.0;
                for (int i = 0; i < bars.Length; i++)
                {
                    double height = bars[i];
                    if (height <= ActiveBandFloor) continue;
                    double d = i - currentIdx;
                    double proximity = Math.Exp(-(d * d) / twoSigmaSq);
                    double score = height * proximity;
                    if (score > bestScore) { bestScore = score; best = i; }
                }
                return best;
            }
            public override Mood Tick(Goal host)
            {
                if (host._moodTime < MinDwell) return this;
                if (host._satiety >= 1.0 || host._moodTime >= MaxDwell) return SatedMood;
                return this;
            }
        }

        /// <summary>
        /// FULL: target the spectrum's <i>anti-centroid</i> — the band index mirroring
        /// the spectral centroid (weighted-average band by amplitude). If energy is
        /// concentrated in bass (centroid ≈ band 10), the anti-centroid is in treble
        /// (≈ band 53) and the mosquito flies right to digest; if a treble passage
        /// pulls the centroid right, the anti-centroid is left and it goes left. The
        /// goal actively pulls AWAY from wherever loudness is concentrated rather than
        /// just picking the marginally-smallest band.
        /// <para>Crucially, the Y target is <b>inverted</b>: instead of hovering above
        /// the anti-centroid bar's top (still inside that band's Gaussian potential —
        /// a mid-strength signal would keep recharging the sensor and contradict the
        /// "discharge" intent), the goal retreats toward the top of the viewport,
        /// genuinely far from every bar. The X choice still steers it away from the
        /// loudest region; the Y choice puts it above the action where distance-based
        /// discharge is plausible.</para>
        /// Satiety drains while away from heat — slowly during transit (charge still
        /// moderate), faster once arrived at the cold spot — and once it empties (or
        /// MaxDwell elapses) we flip back to <see cref="Feeding"/>. Entry satiety
        /// overrides to 1.0 (just got full).
        /// </summary>
        private sealed class Sated : Mood
        {
            /// <summary>Highest retreat altitude (smallest Y fraction — near top of viewport). At this end the goal is hardest to reach: only the high-energy balls can lob into it.</summary>
            private const double MinRetreatYFrac = 0.10;

            /// <summary>Lowest retreat altitude (largest Y fraction — mid-screen). At this end even the beach ball has a plausible "slow lob" window. Chosen below the half-line so the retreat still reads as <i>retreat</i> rather than just "hovering somewhere else".</summary>
            private const double MaxRetreatYFrac = 0.55;

            public override string Name => "Sated";
            public override double InitialSatiety => 1.0;

            /// <summary>Roll a fresh retreat altitude every time we enter Sated, so the cycle doesn't stale-park in the same corner and occasionally drops to a lobbable height.</summary>
            public override void OnEnter(Goal host)
            {
                host._satedRetreatYFrac = MinRetreatYFrac +
                    Random.Shared.NextDouble() * (MaxRetreatYFrac - MinRetreatYFrac);
            }

            public override double ResolveTargetY(Goal host, int band, double viewportH, float[] barHeights)
                => viewportH * host._satedRetreatYFrac;

            public override int PickBand(Goal host)
            {
                var bars = host._bars.BarHeights;
                double sumQ = 0, sumWQ = 0;
                for (int i = 0; i < bars.Length; i++)
                {
                    double q = bars[i];
                    if (q <= 0) continue;
                    sumWQ += i * q;
                    sumQ  += q;
                }
                if (sumQ <= 0) return -1;  // silence → drift to spawn
                double centroid = sumWQ / sumQ;
                int anti = (int)Math.Round((bars.Length - 1) - centroid);
                return Math.Clamp(anti, 0, bars.Length - 1);
            }
            public override Mood Tick(Goal host)
            {
                if (host._moodTime < MinDwell) return this;
                if (host._satiety <= 0.0 || host._moodTime >= MaxDwell) return FeedingMood;
                return this;
            }
        }
    }
    #endregion
}
