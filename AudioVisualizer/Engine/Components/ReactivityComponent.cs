using System;
using System.Windows;

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
    /// Map incoming audio band data into entity state.
    /// Default no-op so subclasses can opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling.</param>
    public virtual void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport) { }
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

        /// <summary>
        /// Scaled bar heights (pixels), updated each frame audio arrives.
        /// </summary>
        public float[] BarHeights { get; private set; } = [];

        /// <summary>
        /// Number of frequency bands being tracked.
        /// </summary>
        public int BandCount => BarHeights.Length;

        /// <inheritdoc />
        public override void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport)
        {
            if (bands.IsEmpty) return;

            if (BarHeights.Length != bands.Length)
                BarHeights = new float[bands.Length];

            // Global max: instant rise, very slow decay
            float currentMax = 0.001f;
            foreach (float v in bands)
                if (v > currentMax) currentMax = v;

            if (currentMax > _globalMax) _globalMax = currentMax;
            else                          _globalMax = Math.Max(0.01f, _globalMax * 0.9995f);

            float scale = (float)viewport.Height / _globalMax;
            for (int i = 0; i < bands.Length; i++)
                BarHeights[i] = bands[i] * scale;
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Ball
    /// <summary>
    /// Beach-ball reactivity: bass triggers upward bounce impulses, treble adds horizontal sway.
    /// </summary>
    public sealed class Ball : ReactivityComponent
    {
        private float _bassEnergy;
        private float _cooldown;

        private const float BounceThreshold = 0.8f;
        private const float BounceImpulse  = 400f;
        private const float CooldownSeconds = 0.3f;

        /// <inheritdoc />
        public override void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport)
        {
            // Always decay cooldown, even when no audio
            _cooldown = Math.Max(0, _cooldown - 0.016f);
            if (bands.IsEmpty) return;

            // Bass bins (first 4)
            float bass = 0;
            int bassCount = Math.Min(4, bands.Length);
            for (int i = 0; i < bassCount; i++) bass += bands[i];
            bass /= bassCount;

            _bassEnergy = Math.Max(bass, _bassEnergy * 0.9f);

            if (_bassEnergy > BounceThreshold && _cooldown <= 0)
            {
                var vel = entity.Velocity;
                vel.Y -= BounceImpulse;
                entity.Velocity = vel;

                _cooldown = CooldownSeconds;
                _bassEnergy = 0;
            }

            // Treble (last 8 bins) → horizontal sway
            if (bands.Length > 10)
            {
                float treble = 0;
                int trebleStart = bands.Length - 8;
                for (int i = trebleStart; i < bands.Length; i++) treble += bands[i];
                treble /= 8;

                if (treble > 0.3f)
                {
                    var vel = entity.Velocity;
                    vel.X += (Random.Shared.NextDouble() - 0.5) * treble * 200;
                    entity.Velocity = vel;
                }
            }
        }
    }
    #endregion
}
