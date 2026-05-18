using System.Collections.ObjectModel;
using WavBall.Configuration;
using WavBall.Models;

namespace WavBall.Services;

/// <summary>
/// Pre-populated list of all ball stages. Times show "-" until completed.
/// Tracks personal bests per kind. The collection order matches <see cref="BallPreset.Stages"/>.
/// </summary>
internal sealed class RoundHistoryStore
{
    private readonly Dictionary<BallKind, TimeSpan> _bests = new();

    /// <summary>One record per ball kind, in stage order. Bind to ListBox.ItemsSource.</summary>
    public ObservableCollection<RoundRecord> Records { get; } = new();

    public RoundHistoryStore()
    {
        // Pre-populate all stages so the full list is visible from the start.
        foreach (var stage in BallPreset.Stages)
            Records.Add(new RoundRecord(stage.Kind, stage.Name));
    }

    /// <summary>Set the current stage highlight (amber).</summary>
    public void SetCurrentStage(int stageIndex)
    {
        for (int i = 0; i < Records.Count; i++)
            Records[i].IsCurrent = (i == stageIndex);
    }

    /// <summary>Record a completed round. Updates the time on the existing entry.</summary>
    public void Complete(BallKind kind, TimeSpan elapsed)
    {
        bool isPb = !_bests.TryGetValue(kind, out var prev) || elapsed < prev;
        if (isPb) _bests[kind] = elapsed;

        // Clear previous latest
        foreach (var r in Records)
            r.IsLatest = false;

        // Find and update the matching record
        var record = Records.FirstOrDefault(r => r.Kind == kind);
        if (record != null)
        {
            record.Elapsed = elapsed;
            record.IsPersonalBest = isPb;
            record.IsLatest = true;
        }
    }

    /// <summary>Returns the personal-best time for a given ball kind, or null if never recorded.</summary>
    public TimeSpan? GetBest(BallKind kind) =>
        _bests.TryGetValue(kind, out var t) ? t : null;
}
