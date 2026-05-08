using System;
using System.Windows;

namespace AudioVisualizer.Engine;

/// <summary>
/// Component that reacts to audio frequency data each frame.
/// Called before physics so that audio-driven state feeds into collision detection.
/// Renamed from IAudioReactiveComponent for symmetry with IPhysicsComponent / IRenderingComponent.
/// </summary>
public interface IReactivityComponent
{
    /// <summary>
    /// Map incoming audio band data into entity state.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling.</param>
    void React(SceneEntity entity, ReadOnlySpan<float> bands, Size viewport);
}
