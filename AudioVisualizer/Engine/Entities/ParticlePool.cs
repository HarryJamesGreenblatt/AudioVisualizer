using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components.Physics;
using AudioVisualizer.Engine.Components.Rendering;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Particle pool entity. Combines the Object Pool pattern (free-list allocation of
/// fixed-size struct buffer) with the Component pattern (Physics + Rendering components
/// that operate on the buffer in batch).
///
/// Particles themselves are <see cref="Particle"/> structs — packed for cache locality
/// and zero GC pressure during gameplay. Treating each particle as its own
/// <see cref="SceneEntity"/> would destroy that performance, so the pool is the entity boundary.
/// </summary>
public sealed class ParticlePool : SceneEntity
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

    #region Properties
    /// <summary>
    /// Direct access to the backing particle array. Physics and render components
    /// iterate this buffer directly to avoid delegate/allocation overhead.
    /// </summary>
    public Particle[] Buffer => _particles;
    #endregion

    #region Constructor
    /// <summary>
    /// Initialize the pool with a given capacity, threading the free list through all slots,
    /// and wire up its Physics and Rendering components.
    /// </summary>
    /// <param name="capacity">Maximum number of concurrent particles.</param>
    public ParticlePool(int capacity = 512)
    {
        _particles = new Particle[capacity];
        for (int i = 0; i < capacity; i++)
            _particles[i] = new Particle();

        // Free list: each particle points to the next available slot
        for (int i = 0; i < capacity - 1; i++)
            _particles[i].NextFree = i + 1;
        _particles[capacity - 1].NextFree = -1;

        _firstAvailable = 0;

        // Wire up components — pool is the entity, components operate on its buffer
        Physics = new ParticlePhysics(this);
        Rendering = new ParticleRenderer(this);
    }
    #endregion

    #region Methods
    /// <summary>
    /// Spawn a particle at the given position with velocity and lifetime.
    /// Returns false (silently) if the pool is full.
    /// </summary>
    public bool Spawn(Point position, Vector velocity, int lifetimeFrames, Color color)
    {
        if (_firstAvailable == -1) return false;

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
    public void SpawnBurst(Point origin, int count, float speed, int lifetime, Color color)
    {
        var rng = Random.Shared;
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            float s = speed * (0.5f + (float)rng.NextDouble());
            var vel = new Vector(Math.Cos(angle) * s, -Math.Abs(Math.Sin(angle)) * s);
            if (!Spawn(origin, vel, lifetime, color))
                break;
        }
    }

    /// <summary>
    /// Tick lifetimes for all live particles and return dead ones to the free list.
    /// Called by ParticlePhysics after the integration phase.
    /// </summary>
    public void TickLifetimes()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];
            if (p.FramesLeft <= 0) continue;

            p.FramesLeft--;
            if (p.FramesLeft == 0)
            {
                p.NextFree = _firstAvailable;
                _firstAvailable = i;
            }
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
