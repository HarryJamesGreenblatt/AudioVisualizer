using AudioVisualizer.Components;

namespace AudioVisualizer.Entities;

/// <summary>
/// Rain emitter entity. Continuously spawns falling drop particles into the shared
/// <see cref="ParticlePool"/>; intensity is modulated by audio amplitude (heavier rain
/// when the music is louder).
///
/// The entity itself has no physics or rendering — drops are particles in the pool
/// and animate via the standard particle pipeline. The pool's physics component
/// (configured with a <see cref="Reactivity.Bar"/> reference) handles
/// drop-vs-bar collision and the single-bounce-then-die behavior.
/// </summary>
public sealed class Rain : World
{
    /// <summary>Construct a rain emitter that spawns drops into the given pool, using bar energy for intensity.</summary>
    public Rain(ParticlePool pool, Reactivity.Bar bars)
    {
        Reactivity = new Reactivity.RainEmitter(pool, bars);
    }
}
