using System.Collections.ObjectModel;
using WavBall.Configuration;
using WavBall.Models;

namespace WavBall.Services;

/// <summary>
/// Maintains one record per ball kind — latest time clobbers the previous.
/// Tracks personal bests per kind. The collection order matches <see cref="BallPreset.Stages"/>.
/// </summary>
internal sealed class RoundHistoryStore
{
    private readonly Dictionary<BallKind, TimeSpan> _bests = new();
    private readonly Dictionary<BallKind, int> _indexByKind = new();

    /// <summary>One record per ball kind, in stage order. Bind to ListBox.ItemsSource.</summary>
    public ObservableCollection<RoundRecord> Records { get; } = new();

    /// <summary>
    /// Index of the most recently updated entry, for highlight cycling.
    /// -1 if no records yet.
    /// </summary>
    public int LastUpdatedIndex { get; private set; } = -1;

    /// <summary>Record a completed round. Clobbers any previous entry for the same ball kind.</summary>
    public void Add(BallKind kind, string ballName, TimeSpan elapsed)
    {
        bool isPb = !_bests.TryGetValue(kind, out var prev) || elapsed < prev;
        if (isPb) _bests[kind] = elapsed;

        var record = new RoundRecord(kind, ballName, elapsed) { IsPersonalBest = isPb, IsLatest = true };

        if (_indexByKind.TryGetValue(kind, out int idx))
        {
            // Clobber existing entry.
            // Clear IsLatest from the previously highlighted slot only when it differs
            // (same slot: old object is being replaced, no need to mutate it).
            if (LastUpdatedIndex >= 0 && LastUpdatedIndex != idx)
                Records[LastUpdatedIndex].IsLatest = false;

            Records[idx] = record;
            LastUpdatedIndex = idx;
        }
        else
        {
            // New kind — clear previous latest, then append.
            if (LastUpdatedIndex >= 0)
                Records[LastUpdatedIndex].IsLatest = false;

            Records.Add(record);
            int newIdx = Records.Count - 1;
            _indexByKind[kind] = newIdx;
            LastUpdatedIndex = newIdx;
        }
    }

    /// <summary>Returns the personal-best time for a given ball kind, or null if never recorded.</summary>
    public TimeSpan? GetBest(BallKind kind) =>
        _bests.TryGetValue(kind, out var t) ? t : null;

    /// <summary>Returns the list index of the given ball kind, or -1 if not yet recorded.</summary>
    public int IndexOf(BallKind kind) =>
        _indexByKind.TryGetValue(kind, out int idx) ? idx : -1;
}
