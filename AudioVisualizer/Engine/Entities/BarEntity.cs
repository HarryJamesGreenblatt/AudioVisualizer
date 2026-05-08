using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Bar spectrum entity. Audio-driven frequency bars across the bottom of the viewport.
/// Owns a <see cref="ReactivityComponent.Bar"/> + <see cref="RenderingComponent.Bar"/>.
/// No physics — bars are pure audio reflections, not simulated.
/// </summary>
public sealed class BarEntity : SceneEntity
{
    /// <summary>
    /// The bar reactivity, exposed so peer entities (e.g. <see cref="PeakEntity"/>) can read live heights.
    /// </summary>
    public ReactivityComponent.Bar Bars { get; }

    /// <summary>Construct a bar entity with reactivity and rendering wired up.</summary>
    public BarEntity()
    {
        Bars = new ReactivityComponent.Bar();
        Reactivity = Bars;
        Rendering = new RenderingComponent.Bar(Bars);
    }
}
