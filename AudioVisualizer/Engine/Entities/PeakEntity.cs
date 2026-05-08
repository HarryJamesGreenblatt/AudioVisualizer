using AudioVisualizer.Engine.Components.Physics;
using AudioVisualizer.Engine.Components.Rendering;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Peak-hold indicator entity. Renders the white markers that ride on top of the spectrum bars,
/// fall under gravity, and bounce when bars push back up into them.
///
/// Holds a reference to a sibling <see cref="BarEntity"/> — its physics queries that entity's
/// bar heights for collision (Component pattern: cross-entity coupling via reference, not inheritance).
/// </summary>
public sealed class PeakEntity : SceneEntity
{
    #region Constructor
    /// <summary>
    /// Construct a peak entity coupled to the given bar entity.
    /// </summary>
    /// <param name="barSource">The bar entity whose heights drive peak collision.</param>
    public PeakEntity(BarEntity barSource)
    {
        var peakPhysics = new PeakPhysics(barSource.Bars);
        Physics = peakPhysics;
        Rendering = new PeakRenderer(barSource.Bars, peakPhysics);
    }
    #endregion
}
