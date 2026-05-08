using System;
using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Abstract base for all audio-reactive behaviors. Subclasses override <see cref="React"/>
/// to map mel-band magnitudes into entity state (position, velocity, internal buffers).
/// Concrete behaviors are nested types so the entire reactivity surface lives in one file.
/// </summary>
public abstract class ReactivityComponent
{
    #region Pipeline
    /// <summary>
    /// Map incoming audio band data into entity state. Default no-op so subclasses opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling.</param>
    /// <param name="dt">Fixed physics timestep in seconds. Use this for cooldowns and continuous-force integration so behavior stays tick-rate independent.</param>
    public virtual void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport, float dt) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Bar
    /// <summary>
    /// Spectrum-bar reactivity: scales each mel-band magnitude into a pixel-space height
    /// via dynamic global-max normalization (instant rise, slow decay).
    /// </summary>
    public sealed class Bar : ReactivityComponent
    {
        private float _globalMax = 0.01f;
        private float[] _prevHeights = [];

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

        /// <inheritdoc />
        public override void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport, float dt)
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
    public sealed class RainEmitter : ReactivityComponent
    {
        #region Fields
        private readonly Entities.ParticlePool _pool;
        private double _spawnAccumulator;

        /// <summary>Baseline drops per second when no audio (or silent audio) is present.</summary>
        private const double BaselineDropsPerSecond = 80.0;

        /// <summary>Maximum drops per second at peak audio amplitude.</summary>
        private const double PeakDropsPerSecond = 200.0;

        /// <summary>Initial downward speed of a freshly spawned drop (px/sec). Picked to look near terminal.</summary>
        private const double SpawnSpeed = 700.0;

        /// <summary>Random horizontal jitter on initial velocity (px/sec). Slight breeze.</summary>
        private const double SpawnHorizontalJitter = 60.0;

        /// <summary>Drop lifetime in 120Hz physics ticks (~1.5 seconds is enough to traverse a tall window).</summary>
        private const int DropLifetime = 180;
        #endregion

        #region Constructor
        /// <summary>Create a rain emitter that spawns drops into the given pool.</summary>
        public RainEmitter(Entities.ParticlePool pool) { _pool = pool; }
        #endregion

        #region Methods
        /// <inheritdoc />
        public override void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport, float dt)
        {
            if (viewport.Width <= 0 || dt <= 0) return;

            // Audio amplitude (mean of all bands) drives spawn-rate intensity. With no audio,
            // amplitude is 0 and rain falls at the baseline rate.
            double amplitude = 0;
            if (!bands.IsEmpty)
            {
                double sum = 0;
                for (int i = 0; i < bands.Length; i++) sum += bands[i];
                amplitude = Math.Clamp(sum / bands.Length, 0, 1);
            }

            double rate = BaselineDropsPerSecond + (PeakDropsPerSecond - BaselineDropsPerSecond) * amplitude;
            _spawnAccumulator += rate * dt;

            var rng = Random.Shared;
            int toSpawn = (int)_spawnAccumulator;
            _spawnAccumulator -= toSpawn;

            // Soft blue-white drop color
            var dropColor = Color.FromArgb(180, 180, 210, 255);

            for (int i = 0; i < toSpawn; i++)
            {
                double x = rng.NextDouble() * viewport.Width;
                double vx = (rng.NextDouble() - 0.5) * 2 * SpawnHorizontalJitter;
                var pos = new Point(x, -8); // start just above the viewport so streak fades in naturally
                var vel = new Vector(vx, SpawnSpeed);
                _pool.SpawnRainDrop(pos, vel, DropLifetime, dropColor);
            }
        }
        #endregion
    }
    #endregion
}
