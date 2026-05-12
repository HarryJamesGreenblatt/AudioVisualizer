using AudioVisualizer.Components;

namespace AudioVisualizer.Entities;

/// <summary>
/// Bar spectrum entity. Audio-driven frequency bars across the bottom of the viewport.
/// Owns a <see cref="Reactivity.Bar"/> + <see cref="Rendering.Bar"/>.
/// No physics — bars are pure audio reflections, not simulated.
/// </summary>
public sealed class Bar : World
{
    /// <summary>
    /// The bar reactivity, exposed so peer entities (e.g. <see cref="Peak"/>) can read live heights.
    /// </summary>
    public Reactivity.Bar Bars { get; }

    /// <summary>Construct a bar entity with reactivity and rendering wired up.</summary>
    public Bar()
    {
        Bars = new Reactivity.Bar();
        Reactivity = Bars;
        Rendering = new Rendering.Bar(Bars);
    }
}
