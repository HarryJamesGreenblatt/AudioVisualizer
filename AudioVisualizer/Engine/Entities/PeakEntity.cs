using AudioVisualizer.Engine.Components;

namespace AudioVisualizer.Engine.Entities;

/// <summary>
/// Peak-hold indicator entity. Renders the white markers that ride atop the spectrum bars,
/// fall under gravity, and bounce when bars push back up into them.
/// Holds a reference to a sibling <see cref="BarEntity"/> — its physics queries that
/// entity's bar heights for collision (cross-entity coupling via constructor reference).
/// </summary>
public sealed class PeakEntity : SceneEntity
{
    /// <summary>Construct a peak entity coupled to the given bar entity.</summary>
    public PeakEntity(BarEntity barSource)
    {
        var peak = new PhysicsComponent.Peak(barSource.Bars);
        Physics = peak;
        Rendering = new RenderingComponent.Peak(barSource.Bars, peak);
    }
}
