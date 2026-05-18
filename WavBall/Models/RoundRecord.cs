using System.ComponentModel;
using WavBall.Configuration;

namespace WavBall.Models;

/// <summary>
/// Data for one game round (one ball type in the stage list).
/// Pre-populated for all stages; time shows "-" until completed.
/// Bound directly to the WMP9-style history ListBox in the side panel.
/// </summary>
public sealed class RoundRecord : INotifyPropertyChanged
{
    public BallKind Kind     { get; }
    public string   BallName { get; }

    private TimeSpan? _elapsed;
    /// <summary>Null until the round is completed.</summary>
    public TimeSpan? Elapsed
    {
        get => _elapsed;
        internal set
        {
            _elapsed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Elapsed)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElapsedFormatted)));
        }
    }

    public bool IsPersonalBest { get; internal set; }

    private bool _isCurrent;
    /// <summary>True for the ball type currently in play. Drives the amber highlight.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    private bool _isLatest;
    /// <summary>True for the most recently completed round.</summary>
    public bool IsLatest
    {
        get => _isLatest;
        internal set
        {
            if (_isLatest == value) return;
            _isLatest = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLatest)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>MM:SS.cc or "-" if not yet completed.</summary>
    public string ElapsedFormatted => _elapsed is { } t
        ? $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}"
        : "-";

    public RoundRecord(BallKind kind, string ballName)
    {
        Kind     = kind;
        BallName = ballName;
    }
}
