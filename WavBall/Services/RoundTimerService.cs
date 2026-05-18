using System.Diagnostics;

namespace WavBall.Services;

/// <summary>
/// Wall-clock stopwatch for one game round. Starts when a ball is spawned, stops
/// when the goal eats it, freezes the final time until the next round begins.
///
/// Four states reflected in <see cref="GetReadout"/>:
///   • Idle    → "● READY"
///   • Running → "▶ MM:SS.cc"   (live counter)
///   • Paused  → "❚❚ MM:SS.cc"  (held; elapsed preserved, resumes on next <see cref="Resume"/>)
///   • Stopped → "■ MM:SS.cc"   (last round's final time, persists until next <see cref="Start"/>)
/// </summary>
internal sealed class RoundTimerService
{
    private readonly Stopwatch _sw = new();
    private TimeSpan _frozen = TimeSpan.Zero;
    private bool _hasRun;
    private bool _paused;

    public bool IsRunning => _sw.IsRunning;
    public bool IsPaused  => _paused;

    /// <summary>Elapsed time at the current moment (live when running, frozen when stopped).</summary>
    public TimeSpan Elapsed => _sw.IsRunning || _paused ? _sw.Elapsed : _frozen;

    /// <summary>Begin a new round. Resets elapsed and starts counting.</summary>
    public void Start()
    {
        _sw.Reset();
        _sw.Start();
        _hasRun = true;
        _paused = false;
    }

    /// <summary>Hold the current elapsed time. Resumes from the same value via <see cref="Resume"/>.</summary>
    public void Pause()
    {
        if (!_sw.IsRunning) return;
        _sw.Stop();   // Stopwatch.Stop preserves elapsed; subsequent Start resumes accumulation.
        _paused = true;
    }

    /// <summary>Resume after <see cref="Pause"/>. No-op if not paused.</summary>
    public void Resume()
    {
        if (!_paused) return;
        _sw.Start();
        _paused = false;
    }

    /// <summary>End the current round. Final time is preserved for display.</summary>
    public void Stop()
    {
        if (!_sw.IsRunning && !_paused) return;
        _frozen = _sw.Elapsed;
        _sw.Stop();
        _paused = false;
    }

    /// <summary>Wipe back to the idle "● READY" state (e.g. when ball toggled off without a goal hit).</summary>
    public void Reset()
    {
        _sw.Reset();
        _frozen = TimeSpan.Zero;
        _hasRun = false;
        _paused = false;
    }

    /// <summary>Pre-formatted LED string for the current state.</summary>
    public string GetReadout()
    {
        if (_sw.IsRunning)
            return "▶ " + Format(_sw.Elapsed);
        if (_paused)
            return "❚❚ " + Format(_sw.Elapsed);
        if (_hasRun)
            return "■ " + Format(_frozen);
        return "● READY";
    }

    private static string Format(TimeSpan t) =>
        $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
}
