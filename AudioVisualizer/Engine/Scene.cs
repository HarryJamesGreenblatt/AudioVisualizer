using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine;

/// <summary>
/// Game Loop + Update Method orchestrator. Owns the entity list, particle pool,
/// and event queue. Runs fixed-timestep physics and variable-rate rendering.
/// </summary>
public sealed class Scene
{
    #region Fields
    /// <summary>
    /// The list of active entities in the scene.
    /// </summary>
    private readonly List<SceneEntity> _entities = new();

    /// <summary>
    /// Physics accumulator for the fixed-timestep loop. Accumulates wall-clock time and allows the physics loop to catch up if rendering is slow.
    /// </summary>
    private float _physicsAccumulator;

    /// <summary>
    /// The pool of particles for visual effects.
    /// </summary>
    private readonly ParticlePool _particles = new(capacity: 512);

    /// <summary>
    /// Scene-level physics systems. Iterated every fixed-timestep tick.
    /// Add new physics concerns via <see cref="AddSystem"/> — no need to touch this class.
    /// </summary>
    private readonly List<IPhysicsSystem> _physicsSystems = new();

    /// <summary>
    /// Event queue for transient events from the audio thread (e.g. beat hits) to be processed on the main thread.
    /// </summary>
    private readonly EventQueue<TransientEvent> _transientQueue = new();
    #endregion

    #region Properties
    /// <summary>
    /// Exposes the transient queue so the audio thread can enqueue events.
    /// </summary>
    public EventQueue<TransientEvent> TransientQueue => _transientQueue;

    /// <summary>
    /// Physics timestep in seconds. A smaller timestep means smoother physics but more CPU usage.
    /// </summary>
    private const float PhysicsDt = 1f / 120f;     // Fixed physics timestep: 120 ticks/sec for smooth gravity
 
    /// <summary>
    /// Number of active entities.
    /// </summary>
    public int EntityCount => _entities.Count;

    /// <summary>
    /// Direct access to the particle pool for spawning effects.
    /// </summary>
    public ParticlePool Particles => _particles;
    #endregion

    #region Constructor
    public Scene()
    {
        _physicsSystems.Add(new ParticlePhysics(_particles));
    }
    #endregion

    #region Methods
    /// <summary>
    /// Register a scene-level physics system. Called once during setup;
    /// the system runs automatically every physics tick from that point on.
    /// </summary>
    public void AddSystem(IPhysicsSystem system) => _physicsSystems.Add(system);

    /// <summary>
    /// Add an entity to the scene.
    /// </summary>
    public void Add(SceneEntity entity) => _entities.Add(entity);

    /// <summary>
    /// Remove an entity from the scene.
    /// </summary>
    public void Remove(SceneEntity entity) => _entities.Remove(entity);

    /// <summary>
    /// Advance the scene by the given wall-clock delta time.
    /// Implements the Game Loop pattern: accumulate time → consume in fixed-dt physics steps.
    /// </summary>
    /// <param name="deltaTime">Seconds elapsed since last Tick call.</param>
    /// <param name="bands">Current audio frequency band data (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport size for position calculations.</param>
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
            foreach (var entity in _entities)
                entity.Update(PhysicsDt, bands);

            // Physics pipeline: forces → integration → collision, per system
            foreach (var system in _physicsSystems)
                system.ApplyForces(PhysicsDt);
            foreach (var system in _physicsSystems)
                system.Integrate(PhysicsDt);
            foreach (var system in _physicsSystems)
                system.ResolveCollisions(PhysicsDt);

            _particles.TickLifetimes();
            _physicsAccumulator -= PhysicsDt;
        }

        // 3. Remove dead entities
        _entities.RemoveAll(e => !e.IsAlive);
    }

    /// <summary>
    /// Render all entities and particles to the given drawing context.
    /// </summary>
    public void Render(DrawingContext dc, Size viewport)
    {
        foreach (var entity in _entities)
            entity.Draw(dc, viewport);

        _particles.RenderAll(dc);
    }
    #endregion
}
