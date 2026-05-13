using WavBall.Components;

namespace WavBall.Entities;

/// <summary>
/// Peak-hold indicator entity. Renders the white markers that ride atop the spectrum bars,
/// fall under gravity, and bounce when bars push back up into them.
/// Holds a reference to a sibling <see cref="Bar"/> — its physics queries that
/// entity's bar heights for collision (cross-entity coupling via constructor reference).
/// </summary>
public sealed class Peak : World
{
    /// <summary>Construct a peak entity coupled to the given bar entity.</summary>
    public Peak(Bar barSource)
    {
        var peak = new Physics.Peak(barSource.Bars);
        Physics = peak;
        Rendering = new Rendering.Peak(barSource.Bars, peak);
    }
}
