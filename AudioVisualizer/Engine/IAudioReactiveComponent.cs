using System;

namespace AudioVisualizer.Engine;

/// <summary>
/// Component that reacts to audio frequency data each frame.
/// Called before physics so that audio-driven positions feed into collision detection.
/// </summary>
public interface IAudioReactiveComponent
{
    /// <summary>
    /// Map incoming audio band data into entity state.
    /// </summary>
    /// <param name="entity">The owning entity whose state may be mutated.</param>
    /// <param name="bands">Current mel-band magnitudes (may be empty if no audio).</param>
    void React(SceneEntity entity, ReadOnlySpan<float> bands);
}
