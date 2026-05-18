using WavBall.Configuration;

namespace WavBall.Models;

/// <summary>
/// Immutable data for one completed game round (ball reached the goal).
/// Bound directly to the WMP9-style history ListBox in the side panel.
/// </summary>
public sealed class RoundRecord
{
    public BallKind Kind          { get; }
    public string   BallName      { get; }
    public TimeSpan Elapsed       { get; }
    public bool     IsPersonalBest { get; internal set; }

    /// <summary>MM:SS.cc formatted elapsed time — bindable property for the DataTemplate.</summary>
    public string ElapsedFormatted =>
        $"{(int)Elapsed.TotalMinutes:D2}:{Elapsed.Seconds:D2}.{Elapsed.Milliseconds / 10:D2}";

    public RoundRecord(BallKind kind, string ballName, TimeSpan elapsed)
    {
        Kind     = kind;
        BallName = ballName;
        Elapsed  = elapsed;
    }
}
