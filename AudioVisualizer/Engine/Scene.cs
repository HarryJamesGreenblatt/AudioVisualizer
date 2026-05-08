using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer.Engine;

/// <summary>
/// Game Loop + Update Method orchestrator. Owns the entity list and event queue.
/// Runs fixed-timestep physics and variable-rate rendering.
///
/// Strict Component pattern: this class knows nothing about specific physics or
/// rendering concerns — it just iterates entities and lets each one's components
/// do the work. Adding a new entity type requires zero changes here.
/// </summary>
public sealed class Scene
{
    #region Fields
    /// <summary>
    /// All active entities in the scene.
    /// </summary>
    private readonly List<SceneEntity> _entities = new();

    /// <summary>
    /// The shared particle pool entity, exposed so external code (e.g. event handlers)
    /// can spawn particles via the Spawn / SpawnBurst methods.
    /// </summary>
    private readonly ParticlePool _particles = new(capacity: 512);

    /// <summary>
    /// Physics accumulator for the fixed-timestep loop.
    /// </summary>
    private float _physicsAccumulator;

    /// <summary>
    /// Event queue for transient events from the audio thread (e.g. beat hits).
    /// </summary>
    private readonly EventQueue<TransientEvent> _transientQueue = new();
    #endregion

    #region Properties
    /// <summary>Exposes the transient queue so the audio thread can enqueue events.</summary>
    public EventQueue<TransientEvent> TransientQueue => _transientQueue;

    /// <summary>Direct access to the particle pool entity for spawning effects.</summary>
    public ParticlePool Particles => _particles;

    /// <summary>Number of active entities (excluding the always-present particle pool).</summary>
    public int EntityCount => _entities.Count;

    /// <summary>
    /// Fixed physics timestep in seconds (120 ticks/sec for smooth gravity).
    /// </summary>
    private const float PhysicsDt = 1f / 120f;
    #endregion

    #region Constructor
    public Scene()
    {
        // The particle pool is itself an entity — added once so its Physics + Rendering
        // components participate in the standard pipeline.
        _entities.Add(_particles);
    }
    #endregion

    #region Methods
    /// <summary>Add an entity to the scene.</summary>
    public void Add(SceneEntity entity) => _entities.Add(entity);

    /// <summary>Remove an entity from the scene.</summary>
    public void Remove(SceneEntity entity) => _entities.Remove(entity);

    /// <summary>
    /// Advance the scene by the given wall-clock delta time.
    /// Game Loop pattern: accumulate time → consume in fixed-dt physics steps.
    /// Each entity self-updates via its components — no system-level dispatch here.
    /// </summary>
    /// <param name="deltaTime">Seconds elapsed since last Tick call.</param>
    /// <param name="bands">Current audio frequency band data (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport size.</param>
    public void Tick(float deltaTime, ReadOnlySpan<float> bands, Size viewport)
    {
        // Cap delta to avoid spiral-of-death when window is dragged or debugger pauses
        float dt = Math.Min(deltaTime, 0.1f);

        // 1. Drain deferred events from the audio thread (Event Queue pattern)
        _transientQueue.DrainAll(evt =>
        {
            _particles.SpawnBurst(evt.Position, count: 8, speed: 150f, lifetime: 30, color: Colors.Orange);
        });

        // 2. Fixed-timestep physics loop (Game Loop pattern)
        _physicsAccumulator += dt;
        while (_physicsAccumulator >= PhysicsDt)
        {
            // Phase A: each entity reacts to audio + applies forces + integrates
            foreach (var entity in _entities)
                entity.Update(PhysicsDt, bands, viewport);

            // Phase B: collision resolution after all entities have moved
            foreach (var entity in _entities)
                entity.ResolveCollisions(PhysicsDt, viewport);

            _physicsAccumulator -= PhysicsDt;
        }

        // 3. Remove dead entities (the particle pool is never marked dead)
        _entities.RemoveAll(e => !e.IsAlive);
    }

    /// <summary>
    /// Render all entities to the given drawing context. Order matters:
    /// background-clearing entities should be added first.
    /// </summary>
    public void Render(DrawingContext dc, Size viewport)
    {
        foreach (var entity in _entities)
            entity.Draw(dc, viewport);
    }
    #endregion
}
