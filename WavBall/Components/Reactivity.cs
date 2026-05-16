using System;
using System.Windows;
using System.Windows.Media;

namespace WavBall.Components;

/// <summary>
/// Abstract base for all audio-reactive behaviors. Subclasses override <see cref="React"/>
/// to map mel-band magnitudes into entity state (position, velocity, internal buffers).
/// Concrete behaviors are nested types so the entire reactivity surface lives in one file.
/// </summary>
public abstract class Reactivity
{
    #region Pipeline
    /// <summary>
    /// Map incoming audio band data into entity state. Default no-op so subclasses opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling.</param>
    /// <param name="dt">Fixed physics timestep in seconds. Use this for cooldowns and continuous-force integration so behavior stays tick-rate independent.</param>
    public virtual void React(World entity, ReadOnlySpan<float> bands, Size viewport, float dt) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Bar
    /// <summary>
    /// Spectrum-bar reactivity: scales each mel-band magnitude into a pixel-space height
    /// via dynamic global-max normalization (instant rise, slow decay).
    /// </summary>
    public sealed class Bar : Reactivity
    {
        private float _globalMax = 0.01f;
        private float[] _prevHeights = [];
        private float[] _prevBands = [];
        private float[] _bandHeat = [];
        private float _energy;
        private float _fluxMax = 0.01f;
        private float _snareFlux;
        private float _snareFluxMax = 0.01f;
        private float _bassFlux;
        private float _bassFluxMax = 0.01f;

        /// <summary>Scaled bar heights (pixels), updated each frame audio arrives.</summary>
        public float[] BarHeights { get; private set; } = [];

        /// <summary>
        /// Per-band screen-space y-velocity of the bar tops (px/sec). Negative = rising
        /// (since +y is down on screen). Consumed by physics components colliding with
        /// the bars to compute proper relative-velocity collision response.
        /// </summary>
        public float[] BarSurfaceVelocities { get; private set; } = [];

        /// <summary>Number of frequency bands being tracked.</summary>
        public int BandCount => BarHeights.Length;

        /// <summary>
        /// Smoothed spectral flux (0–1). Measures how much the frequency spectrum is
        /// changing frame-to-frame — transient density, rhythmic activity, musical "energy".
        /// Half-wave rectified so only onsets (new spectral content) contribute, not decays.
        /// High during drum hits, busy passages, attacks. Near zero during sustains, silence.
        /// </summary>
        public float Energy => _energy;

        /// <summary>
        /// Snare-band spectral flux (0–1). Isolated to mel bands 12–25 (~1–5 kHz)
        /// where snare body + wire energy concentrates. Spikes hard on snare hits,
        /// near-zero otherwise. Use for burst particle spawning on snare transients.
        /// </summary>
        public float SnareFlux => _snareFlux;

        /// <summary>
        /// Bass-band spectral flux (0–1). Isolated to mel bands 0–7 (~30–200 Hz)
        /// where sub-bass and kick fundamentals live. Spikes on kick hits and bass-note
        /// onsets, near-zero on melodic/treble passages with no low-end activity.
        /// Symmetric counterpart to <see cref="SnareFlux"/> — use one for treble-driven
        /// pulses and the other for bass-driven swells so the two can throb independently.
        /// </summary>
        public float BassFlux => _bassFlux;

        /// <summary>
        /// Per-band thermal charge (0–1). Each band accumulates heat from sustained
        /// activity in its frequency region and cools down exponentially when idle.
        /// Drives per-bar luminosity: cold bars are dim, hot bars glow bright.
        /// </summary>
        public float[] BandHeat => _bandHeat;

        /// <inheritdoc />
        public override void React(World entity, ReadOnlySpan<float> bands, Size viewport, float dt)
        {
            // Even when no audio arrives, decay surface velocities toward zero so a
            // stale spike doesn't haunt the next contact.
            if (bands.IsEmpty)
            {
                float decay = MathF.Max(0f, 1f - dt * 8f); // ~125ms time constant
                for (int i = 0; i < BarSurfaceVelocities.Length; i++)
                    BarSurfaceVelocities[i] *= decay;
                return;
            }

            if (BarHeights.Length != bands.Length)
            {
                BarHeights = new float[bands.Length];
                _prevHeights = new float[bands.Length];
                _prevBands = new float[bands.Length];
                _bandHeat = new float[bands.Length];
                BarSurfaceVelocities = new float[bands.Length];
            }

            // Global max: instant rise, very slow decay
            float currentMax = 0.001f;
            foreach (float v in bands)
                if (v > currentMax) currentMax = v;

            if (currentMax > _globalMax) _globalMax = currentMax;
            else                          _globalMax = Math.Max(0.01f, _globalMax * 0.9995f);

            float scale = (float)viewport.Height / _globalMax;
            for (int i = 0; i < bands.Length; i++)
                BarHeights[i] = bands[i] * scale;

            // Surface velocity: heights grow upward → screen-space y-velocity is the negation.
            // Smoothed with a one-pole IIR so a single jumpy audio frame doesn't punch the ball.
            if (dt > 0)
            {
                const float alpha = 0.15f; // weight of the new sample (0–1); lower = smoother but laggier
                for (int i = 0; i < bands.Length; i++)
                {
                    float instant = -(BarHeights[i] - _prevHeights[i]) / dt;
                    BarSurfaceVelocities[i] = (1 - alpha) * BarSurfaceVelocities[i] + alpha * instant;
                    _prevHeights[i] = BarHeights[i];
                }
            }

            // ─── Per-band heat: charge from activity, exponential cooldown ───
            // Each band is like a xylophone key that glows when struck. Sustained
            // activity keeps it hot; silence lets it cool. Drives per-bar luminosity.
            //
            // Frequency-dependent sensitivity: high-frequency bands have lower absolute
            // magnitudes on mel scale, so a hi-hat trill would never glow without help.
            // We apply a sensitivity ramp: band 0 gets 1×, band 63 gets ~3× charge rate
            // and ~1.5× slower cooling. This lets busy high-freq content accumulate heat
            // proportional to its perceptual activity rather than its raw magnitude.
            const float baseChargeRate = 4.0f;
            const float baseCooldown = 0.6f;      // seconds, time constant at band 0
            const float sensitivityMax = 3.0f;     // charge multiplier at highest band
            const float cooldownStretch = 1.0f;    // no extra cooling slowdown at treble

            for (int i = 0; i < bands.Length; i++)
            {
                float t = (float)i / Math.Max(1, bands.Length - 1); // 0 at bass, 1 at treble
                float sensitivity = 1.0f + (sensitivityMax - 1.0f) * t;
                float cooldownTau = baseCooldown * (1.0f + (cooldownStretch - 1.0f) * t);

                float coolFactor = MathF.Exp(-dt / cooldownTau);
                float normalizedBand = bands[i] / _globalMax;
                _bandHeat[i] = _bandHeat[i] * coolFactor + normalizedBand * baseChargeRate * sensitivity * dt;
                _bandHeat[i] = Math.Min(_bandHeat[i], 1.0f);
            }

            // ─── Spectral flux: sum of positive band-to-band differences (onsets only) ───
            // This measures how much NEW spectral content appeared since last frame.
            // Unlike RMS (loudness), flux has wide dynamic range: near-zero on sustains,
            // peaks hard on transient attacks. Normalized against a tracking max.
            // Snare band range: mel bands 12–25 (~1–5 kHz: snare body + wire shimmer)
            // Bass band range:  mel bands  0– 7 (~30–200 Hz: sub-bass + kick fundamentals)
            const int snareLoB = 12;
            const int snareHiB = 25;
            const int bassLoB  = 0;
            const int bassHiB  = 7;

            float flux = 0;
            float snareRawFlux = 0;
            float bassRawFlux = 0;
            for (int i = 0; i < bands.Length; i++)
            {
                float diff = bands[i] - _prevBands[i];
                if (diff > 0)
                {
                    flux += diff;
                    if (i >= snareLoB && i <= snareHiB) snareRawFlux += diff;
                    if (i >= bassLoB  && i <= bassHiB)  bassRawFlux  += diff;
                }
                _prevBands[i] = bands[i];
            }

            // Adaptive normalization: track observed flux max (fast rise, ~1.5s decay)
            // Fast decay lets subsequent hits reach 1.0 even if earlier ones were bigger.
            if (flux > _fluxMax) _fluxMax = flux;
            else _fluxMax = Math.Max(0.01f, _fluxMax * MathF.Exp(-dt / 1.5f));

            float instantEnergy = Math.Clamp(flux / _fluxMax, 0f, 1f);

            // Snare flux: same adaptive normalization, faster decay (~0.8s) so
            // individual snare hits reliably reach 1.0 even after a loud fill.
            if (snareRawFlux > _snareFluxMax) _snareFluxMax = snareRawFlux;
            else _snareFluxMax = Math.Max(0.01f, _snareFluxMax * MathF.Exp(-dt / 0.8f));

            float instantSnare = Math.Clamp(snareRawFlux / _snareFluxMax, 0f, 1f);
            // Very fast attack (~5ms), moderate release (~100ms) — snappy transient detection
            float snareAttack = 1f - MathF.Exp(-dt / 0.005f);
            float snareRelease = 1f - MathF.Exp(-dt / 0.10f);
            float snareAlpha = instantSnare > _snareFlux ? snareAttack : snareRelease;
            _snareFlux = _snareFlux + snareAlpha * (instantSnare - _snareFlux);
            _snareFlux = Math.Clamp(_snareFlux, 0f, 1f);

            // Bass flux: same adaptive normalization, slightly longer decay (~1.2s) so
            // sustained sub-bass keeps the halo swelling between kicks rather than
            // re-collapsing every ~half-second. Bass attack is also a touch slower than
            // snare attack since kick fundamentals are broader transients perceptually.
            if (bassRawFlux > _bassFluxMax) _bassFluxMax = bassRawFlux;
            else _bassFluxMax = Math.Max(0.01f, _bassFluxMax * MathF.Exp(-dt / 1.2f));

            float instantBass = Math.Clamp(bassRawFlux / _bassFluxMax, 0f, 1f);
            float bassAttack  = 1f - MathF.Exp(-dt / 0.020f);  // ~20ms
            float bassRelease = 1f - MathF.Exp(-dt / 0.25f);   // ~250ms
            float bassAlpha = instantBass > _bassFlux ? bassAttack : bassRelease;
            _bassFlux = _bassFlux + bassAlpha * (instantBass - _bassFlux);
            _bassFlux = Math.Clamp(_bassFlux, 0f, 1f);

            // Asymmetric smoothing: very fast attack (~15ms), moderate release (~150ms)
            // so rain/luminosity respond to individual hits but don't strobe.
            float attackAlpha = 1f - MathF.Exp(-dt / 0.015f);
            float releaseAlpha = 1f - MathF.Exp(-dt / 0.15f);
            float smoothAlpha = instantEnergy > _energy ? attackAlpha : releaseAlpha;
            _energy = _energy + smoothAlpha * (instantEnergy - _energy);
            _energy = Math.Clamp(_energy, 0f, 1f);
        }
    }
    #endregion

    // ────────────────────────────────────────────────────────────────────────
    #region Nested: RainEmitter
    /// <summary>
    /// Continuous rain emitter. Spawns drops across the top of the viewport at a baseline
    /// rate, optionally scaled up by audio amplitude (heavier rain when the music is louder).
    /// Owns no physics or rendering of its own — the spawned drops are particles in the
    /// shared <see cref="Entities.ParticlePool"/> and animate via the standard particle pipeline.
    /// </summary>
    public sealed class RainEmitter : Reactivity
    {
        #region Fields
        private readonly Entities.ParticlePool _pool;
        private readonly Bar _bars;
        private double _spawnAccumulator;
        private double _snarePrevFlux;
        private double _snareBurstCooldown;

        // Wind state — now interpreted as the AIR'S VELOCITY (px/sec), not acceleration.
        // Each drop's drag accelerates it toward this velocity at a rate inversely proportional
        // to its mass, so the same wind field produces visibly different motion across the
        // population. No more low-pass chase: the drops themselves provide the smoothing
        // through their drag-driven velocity matching.
        private Vector _wind;
        private double _bassEnvelope;
        private double _bassPrev;
        private double _gustCooldown;

        /// <summary>Baseline drops per second when no audio (or silent audio) is present.</summary>
        private const double BaselineDropsPerSecond = 80.0;

        /// <summary>Maximum drops per second at peak audio amplitude.</summary>
        private const double PeakDropsPerSecond = 900.0;

        /// <summary>Drop lifetime in 120Hz physics ticks (~1.7s).</summary>
        private const int DropLifetime = 200;

        /// <summary>Minimum size factor for spawned drops.</summary>
        private const double SizeMin = 0.35;

        /// <summary>Maximum size factor for spawned drops.</summary>
        private const double SizeMax = 1.8;

        /// <summary>
        /// Bias exponent for the size distribution: <c>size = SizeMin + (SizeMax−SizeMin) · rand^bias</c>.
        /// Higher = more biased toward small drops (matches real exponential-like rain DSD).
        /// Moderate bias (2.0) keeps a realistic skew while ensuring enough medium/large drops
        /// exist that the parallax-speed variation is clearly visible.
        /// </summary>
        private const double SizeDistributionBias = 2.0;

        /// <summary>Reference gravity (matches PhysicsComponent.Particle.Gravity, used to compute spawn-velocity).</summary>
        private const double GravityRef = 600.0;

        /// <summary>Reference linear drag coefficient (matches PhysicsComponent.Particle.DragCoefficient).</summary>
        private const double DragRef = 1.714;

        // Wind impulse model. Silence ⇒ still air. Bass attacks nudge the wind VELOCITY
        // (gently, with horizontal continuity), and small drops chase it more eagerly than
        // big drops thanks to size-dependent drag.

        /// <summary>Bass intensity (smoothed) above which a gust may fire.</summary>
        private const double GustTriggerThreshold = 0.45;

        /// <summary>Minimum jump in bass intensity vs. previous frame to count as a transient.</summary>
        private const double GustTransientDelta = 0.08;

        /// <summary>Per-gust velocity nudge magnitude (px/sec) added to the wind. Compounds across hits.</summary>
        private const double GustNudge = 60.0;

        /// <summary>Maximum wind velocity magnitude (px/sec). Caps runaway accumulation.</summary>
        private const double MaxWindMagnitude = 250.0;

        /// <summary>Per-second exponential decay of wind velocity back to zero.</summary>
        private const double WindDecay = 0.5; // ~2s time constant

        /// <summary>Real-time seconds between consecutive gust triggers.</summary>
        private const double GustCooldownSeconds = 0.18;

        /// <summary>Probability that a gust reverses direction (left↔right).</summary>
        private const double DirectionFlipProbability = 0.40;
        #endregion

        #region Constructor
        /// <summary>Create a rain emitter that spawns drops into the given pool, using bar energy for intensity.</summary>
        public RainEmitter(Entities.ParticlePool pool, Bar bars) { _pool = pool; _bars = bars; }
        #endregion

        #region Methods
        /// <inheritdoc />
        public override void React(World entity, ReadOnlySpan<float> bands, Size viewport, float dt)
        {
            if (viewport.Width <= 0 || dt <= 0) return;

            // ─── Bass envelope (for gust triggering) ───
            double bassNow = 0;
            if (!bands.IsEmpty)
            {
                int bassN = Math.Min(4, bands.Length);
                for (int i = 0; i < bassN; i++) bassNow += bands[i];
                bassNow /= bassN;
            }

            const double envAlpha = 0.4;
            _bassEnvelope = (1 - envAlpha) * _bassEnvelope + envAlpha * bassNow;
            _gustCooldown = Math.Max(0, _gustCooldown - dt);
            double bassDelta = _bassEnvelope - _bassPrev;
            _bassPrev = _bassEnvelope;

            // ─── Gust trigger: bass pushes rain AWAY from the low-frequency source ───
            // Bass activity lives in the leftmost bands. We compute a spectral "center of
            // mass" across the bottom 8 bands: if energy is concentrated left, wind blows
            // RIGHT (pushing rain away from the source). This fills the right side naturally
            // during bass-heavy passages instead of pulling everything toward the bass.
            if (_gustCooldown <= 0 && _bassEnvelope > GustTriggerThreshold && bassDelta > GustTransientDelta)
            {
                // Spectral center of mass over bass region (bands 0–7)
                double weightedSum = 0, totalWeight = 0;
                int bassRegion = Math.Min(8, bands.Length);
                for (int i = 0; i < bassRegion; i++)
                {
                    weightedSum += i * (double)bands[i];
                    totalWeight += bands[i];
                }
                // center: 0 = all energy in band 0 (far left), 1 = all in band 7 (toward center)
                double center = totalWeight > 0.001 ? weightedSum / (totalWeight * (bassRegion - 1)) : 0.5;

                // Push AWAY from the energy source: energy left of center → wind blows right (+1)
                // Small random perturbation keeps it organic.
                int direction = center < 0.5 ? 1 : -1;
                if (Random.Shared.NextDouble() < DirectionFlipProbability) direction = -direction;

                double scale = GustNudge * Math.Min(1.0, _bassEnvelope * 1.5);
                _wind.X += direction * scale;
                if (Math.Abs(_wind.X) > MaxWindMagnitude)
                    _wind.X = Math.Sign(_wind.X) * MaxWindMagnitude;

                _gustCooldown = GustCooldownSeconds;
            }

            // ─── Wind exponential decay toward zero ( ambient air comes to rest if no new gusts ) ───
            _wind *= Math.Exp(-WindDecay * dt);

            if (_pool.Physics is Physics.Particle pp)
                pp.Wind = _wind;

            // ─── Spawn drops with per-drop varied size — intensity driven by smoothed energy ───
            double energy = _bars.Energy;
            double rate = BaselineDropsPerSecond + (PeakDropsPerSecond - BaselineDropsPerSecond) * energy;
            _spawnAccumulator += rate * dt;

            // ─── Snare burst: detect transient onset in snare band, fire a burst ───
            double snareNow = _bars.SnareFlux;
            double snareDelta = snareNow - _snarePrevFlux;
            _snarePrevFlux = snareNow;
            _snareBurstCooldown = Math.Max(0, _snareBurstCooldown - dt);

            const double snareBurstThreshold = 0.25;   // minimum flux jump to trigger
            const double snareBurstCooldownSec = 0.08;  // prevent double-triggering
            const int snareBurstCount = 50;             // particles per burst

            bool snareFired = false;
            if (_snareBurstCooldown <= 0 && snareDelta > snareBurstThreshold && snareNow > 0.3)
            {
                _snareBurstCooldown = snareBurstCooldownSec;
                _spawnAccumulator += snareBurstCount;
                snareFired = true;
            }

            var rng = Random.Shared;
            int toSpawn = (int)_spawnAccumulator;
            _spawnAccumulator -= toSpawn;

            // Bright blue-white drop color (alpha will be further modulated by size in the renderer)
            var dropColor = Color.FromArgb(255, 200, 220, 255);

            for (int i = 0; i < toSpawn; i++)
            {
                // Burst drops are biased larger for visual impact
                double biasExponent = snareFired ? 1.0 : SizeDistributionBias;
                double sizeRand = Math.Pow(rng.NextDouble(), biasExponent);
                float size = (float)(SizeMin + (SizeMax - SizeMin) * sizeRand);

                // Spawn at the drop's natural terminal velocity (∝ size for linear drag)
                // so it doesn't accelerate visibly after entering frame — looks like rain
                // that's already been falling.
                double terminalY = GravityRef * size / DragRef;

                double x = rng.NextDouble() * viewport.Width;
                var pos = new Point(x, -8); // start just above the viewport
                var vel = new Vector(0, terminalY);
                _pool.SpawnRainDrop(pos, vel, DropLifetime, dropColor, size);
            }
        }
        #endregion
    }
    #endregion
}
