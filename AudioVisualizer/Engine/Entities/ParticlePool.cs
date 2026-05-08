using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components;

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
    /// <param name="bars">Optional bar reactivity — enables rain-drop bouncing off the bar surface.</param>
    public ParticlePool(int capacity = 512, ReactivityComponent.Bar? bars = null)
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
        Physics   = new PhysicsComponent.Particle(this, bars);
        Rendering = new RenderingComponent.Particle(this);
    }
    #endregion

    #region Methods
    /// <summary>
    /// Spawn a particle at the given position with velocity, lifetime, kind, and size.
    /// Returns false (silently) if the pool is full.
    /// </summary>
    /// <param name="size">Relative drop/particle size in dimensionless units (1.0 = reference). Drives mass (∝ size³) and drag area (∝ size²), which together produce a per-drop terminal velocity ∝ √size and per-drop wind responsiveness ∝ 1/size.</param>
    public bool Spawn(Point position, Vector velocity, int lifetimeFrames, Color color, ParticleKind kind = ParticleKind.Spark, float size = 1.0f)
    {
        if (_firstAvailable == -1) return false;

        int slot = _firstAvailable;
        ref var p = ref _particles[slot];
        _firstAvailable = p.NextFree;

        p.Position = position;
        p.Velocity = velocity;
        p.FramesLeft = lifetimeFrames;
        p.Color = color;
        p.Kind = kind;
        p.Size = size;
        p.BounceUsed = false;
        p.NextFree = -1;
        // Reset trail history so we don't carry stale points from this slot's previous life.
        p.TrailLen = 0;
        p.Trail0 = position;
        p.Trail1 = position;
        p.Trail2 = position;
        p.Trail3 = position;
        return true;
    }

    /// <summary>
    /// Spawn a burst of spark particles radiating outward from a point. Used for
    /// transient/impact effects. Spawned particles are <see cref="ParticleKind.Spark"/>.
    /// </summary>
    public void SpawnBurst(Point origin, int count, float speed, int lifetime, Color color)
    {
        var rng = Random.Shared;
        for (int i = 0; i < count; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            float s = speed * (0.5f + (float)rng.NextDouble());
            var vel = new Vector(Math.Cos(angle) * s, -Math.Abs(Math.Sin(angle)) * s);
            if (!Spawn(origin, vel, lifetime, color, ParticleKind.Spark))
                break;
        }
    }

    /// <summary>
    /// Spawn a single rain drop at the given position with the given size. Drops are
    /// pre-flagged so the renderer streaks them along their velocity, and physics applies
    /// per-drop drag based on size (smaller = more affected by wind, lower terminal velocity).
    /// </summary>
    public bool SpawnRainDrop(Point position, Vector velocity, int lifetimeFrames, Color color, float size = 1.0f)
    {
        return Spawn(position, velocity, lifetimeFrames, color, ParticleKind.RainDrop, size);
    }

    /// <summary>
    /// Tick lifetimes for all live particles and return dead ones to the free list.
    /// Called by ParticlePhysics after the integration phase. Idempotently reclaims
    /// any slot whose <c>FramesLeft</c> reached \u2264 0 (whether by countdown or by an
    /// external kill from collision code) and isn't already linked into the free list.
    /// </summary>
    public void TickLifetimes()
    {
        for (int i = 0; i < _particles.Length; i++)
        {
            ref var p = ref _particles[i];

            if (p.FramesLeft > 0)
            {
                p.FramesLeft--;
                if (p.FramesLeft != 0) continue;
            }

            // FramesLeft is 0. Reclaim the slot if it isn't already in the free list.
            // Live slots are flagged with NextFree == -1 in Spawn; a slot that has been
            // killed externally (e.g. drop-on-second-contact) is also -1 and needs reclaiming.
            if (p.NextFree == -1)
            {
                p.NextFree = _firstAvailable;
                _firstAvailable = i;
            }
        }
    }
    #endregion

    #region Nested Types
    /// <summary>
    /// Categorizes how a particle is rendered and how physics treats it.
    /// One byte in the struct; renderers/physics components dispatch on this.
    /// </summary>
    public enum ParticleKind : byte
    {
        /// <summary>Default: small alpha-fading dot, no surface collision (legacy bursts/sparks).</summary>
        Spark = 0,

        /// <summary>Falling rain drop: streaked along velocity, bounces once off surfaces, then dies.</summary>
        RainDrop = 1,
    }

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

        /// <summary>
        /// Relative size in dimensionless units (1.0 = reference drop). Drives per-drop
        /// mass (∝ size³), drag area (∝ size²), and therefore terminal velocity (∝ √size)
        /// and wind responsiveness (∝ 1/size). The visual streak width and opacity
        /// also scale with this value.
        /// </summary>
        public float Size;

        /// <summary>Render/physics kind. See <see cref="ParticleKind"/>.</summary>
        public ParticleKind Kind;

        /// <summary>True once a kind-eligible particle has consumed its single allowed bounce.</summary>
        public bool BounceUsed;

        /// <summary>Index of next free slot in the pool, or -1 if end of list / in-use.</summary>
        public int NextFree;

        // ── Trail history (motion-blur rendering) ──
        // Inline ring buffer of past positions. The renderer draws a polyline through
        // [Trail3 → Trail2 → Trail1 → Trail0 → Position], so when the drop curves under
        // wind, the streak naturally bends to follow the actual trajectory rather than
        // being a rigid line locked to the current velocity vector. Sampled at integration
        // time. Cleared on bounce so the post-bounce trail starts fresh from the impact.

        /// <summary>Most recent history point (1 step before current Position).</summary>
        public Point Trail0;

        /// <summary>Second-oldest history point.</summary>
        public Point Trail1;

        /// <summary>Third-oldest history point.</summary>
        public Point Trail2;

        /// <summary>Oldest history point in the trail.</summary>
        public Point Trail3;

        /// <summary>Number of valid trail points (0–4). Grows from 0 as the drop ages.</summary>
        public byte TrailLen;
    }
    #endregion
}
