using System;
using System.Windows;

namespace AudioVisualizer.Engine.Components.Reactivity;

/// <summary>
/// Reactivity component for beach balls.
/// Bass frequencies trigger upward bounce impulses; treble adds gentle horizontal sway.
/// Mutates the entity's Velocity directly — physics integrates on the next tick.
/// </summary>
public sealed class BallReactivity : IReactivityComponent
{
    #region Fields
    /// <summary>
    /// Bass energy accumulator for triggering bounces (decays each frame).
    /// </summary>
    private float _bassEnergy;

    /// <summary>
    /// Cooldown timer (seconds) to prevent rapid-fire bounces.
    /// </summary>
    private float _cooldown;

    /// <summary>
    /// Bass intensity threshold above which a bounce is triggered.
    /// </summary>
    private const float BounceThreshold = 0.8f;

    /// <summary>
    /// Upward impulse magnitude applied on bass triggers (pixels/sec).
    /// </summary>
    private const float BounceImpulse = 400f;

    /// <summary>
    /// Cooldown duration in seconds between consecutive bounces.
    /// </summary>
    private const float CooldownSeconds = 0.3f;
    #endregion

    #region Methods
    /// <inheritdoc />
    public void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport)
    {
        // Always decay cooldown, even when no audio
        _cooldown = Math.Max(0, _cooldown - 0.016f);

        if (bands.IsEmpty) return;

        // Sample bass bins (first 4)
        float bass = 0;
        int bassCount = Math.Min(4, bands.Length);
        for (int i = 0; i < bassCount; i++)
            bass += bands[i];
        bass /= bassCount;

        // Accumulate bass energy with decay
        _bassEnergy = Math.Max(bass, _bassEnergy * 0.9f);

        // Trigger bounce if threshold exceeded and cooldown expired
        if (_bassEnergy > BounceThreshold && _cooldown <= 0)
        {
            var vel = entity.Velocity;
            vel.Y -= BounceImpulse;
            entity.Velocity = vel;

            _cooldown = CooldownSeconds;
            _bassEnergy = 0;
        }

        // Treble adds horizontal sway
        if (bands.Length > 10)
        {
            float treble = 0;
            int trebleStart = bands.Length - 8;
            for (int i = trebleStart; i < bands.Length; i++)
                treble += bands[i];
            treble /= 8;

            if (treble > 0.3f)
            {
                var vel = entity.Velocity;
                vel.X += (Random.Shared.NextDouble() - 0.5) * treble * 200;
                entity.Velocity = vel;
            }
        }
    }
    #endregion
}
