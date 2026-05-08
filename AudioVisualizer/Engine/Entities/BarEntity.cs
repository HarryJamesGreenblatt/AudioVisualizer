using AudioVisualizer.Engine.Components.Reactivity;
using AudioVisualizer.Engine.Components.Rendering;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Bar spectrum entity. Represents the audio-driven frequency bars across the bottom of the viewport.
/// Owns a BarReactivity (audio → heights) and BarRenderer (draws bars + clears background).
/// No Physics component — bars are purely audio-driven, not simulated.
/// </summary>
public sealed class BarEntity : SceneEntity
{
    #region Properties
    /// <summary>
    /// The bar reactivity component, exposed so peer entities (e.g. PeakEntity) can read live heights.
    /// </summary>
    public BarReactivity Bars { get; }
    #endregion

    #region Constructor
    /// <summary>
    /// Construct a bar entity with reactivity and rendering wired up.
    /// </summary>
    public BarEntity()
    {
        Bars = new BarReactivity();
        Reactivity = Bars;
        Rendering = new BarRenderer(Bars);
    }
    #endregion
}
