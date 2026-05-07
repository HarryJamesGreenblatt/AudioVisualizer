using System;
using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer.Engine;

/// <summary>
/// Object Pool pattern: pre-allocated fixed array of particles with a free-list
/// for O(1) allocation/deallocation. No GC pressure during gameplay.
/// </summary>
public sealed class ParticlePool
{
    #region Fields
    /// <summary>
    /// Fixed-size array of particles. All slots are pre-allocated at construction.
    /// </summary>
    private readonly Particle[] _particles;

    /// <summary>
    /// Index of the first available (dead) particle in the free list, or -1 if pool is full.
    /// </summary>
    private int _firstAvailable;
    #endregion

    #region Constructor
    /// <summary>
    /// Initialize the pool with a given capacity, threading the free list through all slots.
    /// </summary>
    /// <param name="capacity">Maximum number of concurrent particles.</param>
    public ParticlePool(int capacity = 512)
    {
        _particles = new Particle[capacity];
        for (int i = 0; i < capacity; i++)
            _particles[i] = new Particle();

        // Initialize free list: each particle points to the next available slot
        for (int i = 0; i < capacity - 1; i++)
            _particles[i].NextFree = i + 1;
        _particles[capacity - 1].NextFree = -1; // end of list

        _firstAvailable = 0;
    }
    #endregion

    #region Methods
    /// <summary>
    /// Spawn a particle at the given position with velocity and lifetime.
    /// Returns false (silently) if the pool is full — the user won't notice one fewer sparkle.
    /// </summary>
    /// <param name="position">World-space spawn point.</param>
    /// <param name="velocity">Initial velocity vector.</param>
    /// <param name="lifetimeFrames">Number of physics ticks before the particle dies.</param>
    /// <param name="color">Base render color (alpha fades with lifetime).</param>
    /// <returns>True if a particle was spawned; false if pool is exhausted.</returns>
    public bool Spawn(Point position, Vector velocity, int lifetimeFrames, Color color)
    {
        if (_firstAvailable == -1)
            return false;

        int slot = _firstAvailable;
        ref var p = ref _particles[slot];
        _firstAvailable = p.NextFree;

        p.Position = position;
        p.Velocity = velocity;
        p.FramesLeft = lifetimeFrames;
        p.Color = color;
        p.NextFree = -1;
        return true;
    }

    /// <summary>
    /// Spawn a burst of particles radiating outward from a point.
    /// </summary>
    /// <param name="origin">World-space center of the burst.</param>
    /// <param name="count">Number of particles to emit.</param>
    /// <param name="speed">Base radial speed (randomized ±50%).</param>
    /// <param name="lifetime">Lifetime in physics ticks.</param>
    /// <param name="color">Base render color.</param>
    public void SpawnBurst(Point origin, int count, float speed, int lifetime, Color color)
    {
        var rng = Random.Shared;
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            float s = speed * (0.5f + (float)rng.NextDouble());
            var vel = new Vector(Math.Cos(angle) * s, -Math.Abs(Math.Sin(angle)) * s);
            if (!Spawn(origin, vel, lifetime, color))
                break; // pool full, stop trying
        }
    }

    /// <summary>
    /// Update Method: advance all live particles one frame.
    /// Dead particles are returned to the free list (O(1)).
    /// </summary>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    public void UpdateAll(float dt)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.FramesLeft <= 0) continue;

            p.FramesLeft--;
            p.Velocity += new Vector(0, 300 * dt); // gravity
            p.Position += p.Velocity * dt;

            if (p.FramesLeft == 0)
            {
                // Return to free list
                p.NextFree = _firstAvailable;
                _firstAvailable = i;
            }
        }
    }

    /// <summary>
    /// Render all live particles to the drawing context.
    /// </summary>
    /// <param name="dc">WPF drawing context for immediate-mode rendering.</param>
    public void RenderAll(DrawingContext dc)
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.FramesLeft <= 0) continue;

            // Fade out as lifetime decreases
            byte alpha = (byte)Math.Clamp(p.FramesLeft * 8, 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, p.Position, 2, 2);
        }
    }
    #endregion

    #region Nested Types
    /// <summary>
    /// Individual particle state. Struct for cache-friendly iteration.
    /// When not alive (FramesLeft ≤ 0), NextFree stores the free-list link.
    /// </summary>
    public struct Particle
    {
        /// <summary>World-space position.</summary>
        public Point Position;

        /// <summary>Current velocity (pixels/second).</summary>
        public Vector Velocity;

        /// <summary>Remaining lifetime in physics ticks. ≤ 0 means dead/available.</summary>
        public int FramesLeft;

        /// <summary>Base render color.</summary>
        public Color Color;

        /// <summary>Index of next free slot in the pool, or -1 if end of list / in-use.</summary>
        public int NextFree;
    }
    #endregion
}
