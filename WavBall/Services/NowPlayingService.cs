using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WavBall.Services;

/// <summary>
/// Wraps GlobalSystemMediaTransportControlsSessionManager to expose current
/// media session metadata (title, artist, thumbnail, source app).
///
/// Shows any session whose playback status is Playing or Paused.
/// When the active session stops, scans all registered sessions for another
/// one that is still active (e.g. browser → Spotify handoff).
/// </summary>
public sealed class NowPlayingService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _trackedSession;
    private bool _disposed;

    public string Title                { get; private set; } = string.Empty;
    public string Artist               { get; private set; } = string.Empty;
    public string SourceAppUserModelId { get; private set; } = string.Empty;
    public IRandomAccessStreamReference? ThumbnailRef { get; private set; }
    public bool HasSession             { get; private set; }

    /// <summary>Fires on a background thread whenever session data changes.</summary>
    public event Action? Changed;

    private NowPlayingService() { }

    public static async Task<NowPlayingService> CreateAsync()
    {
        var svc = new NowPlayingService();
        svc._manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        svc._manager.CurrentSessionChanged += svc.OnCurrentSessionChanged;
        svc._manager.SessionsChanged       += svc.OnSessionsChanged;
        await svc.AttachSessionAsync();
        return svc;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) => await AttachSessionAsync();

    // A new app started (or stopped) a media session — re-scan all sessions.
    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) => await RefreshAsync();

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await RefreshAsync();

    // Play / pause / stop transitions on the tracked session.
    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => await RefreshAsync();

    // ── Session tracking ──────────────────────────────────────────────────────

    private async Task AttachSessionAsync()
    {
        // Detach from old session's per-session events.
        if (_trackedSession != null)
        {
            _trackedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _trackedSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
            _trackedSession = null;
        }

        // Attach to the new current session.
        _trackedSession = _manager?.GetCurrentSession();
        if (_trackedSession != null)
        {
            _trackedSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _trackedSession.PlaybackInfoChanged    += OnPlaybackInfoChanged;
        }

        await RefreshAsync();
    }

    // ── Metadata refresh ──────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        if (_manager == null) { ClearAndNotify(); return; }

        // Pick the best session to display:
        //   1. Current session if it is Playing or Paused
        //   2. Any other registered session that is Playing or Paused
        //   3. Nothing — hide the panel
        var best = FindBestSession();

        if (best == null) { ClearAndNotify(); return; }

        try
        {
            var props = await best.TryGetMediaPropertiesAsync();
            Title                = props?.Title  ?? string.Empty;
            Artist               = props?.Artist ?? string.Empty;
            SourceAppUserModelId = best.SourceAppUserModelId ?? string.Empty;
            ThumbnailRef         = props?.Thumbnail;
            HasSession           = !string.IsNullOrEmpty(Title) || !string.IsNullOrEmpty(Artist);
        }
        catch
        {
            HasSession = false;
        }
        Changed?.Invoke();
    }

    private GlobalSystemMediaTransportControlsSession? FindBestSession()
    {
        var current  = _manager!.GetCurrentSession();
        var sessions = _manager.GetSessions();

        if (current != null && IsActive(current)) return current;

        foreach (var s in sessions)
            if (IsActive(s)) return s;

        return null;
    }

    private static bool IsActive(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var status = session.GetPlaybackInfo()?.PlaybackStatus;
            return status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                          or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
        }
        catch { return false; }
    }

    private void ClearAndNotify()
    {
        HasSession = false;
        Title = Artist = SourceAppUserModelId = string.Empty;
        ThumbnailRef = null;
        Changed?.Invoke();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a raw AUMID into a short, human-readable app name.
    /// e.g. "Spotify.exe" → "Spotify", "SpotifyAB.SpotifyMusic_xxx!Spotify" → "Spotify"
    /// </summary>
    public static string FormatAppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return string.Empty;

        // Unpackaged desktop app: ends with .exe
        if (aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFileNameWithoutExtension(aumid);

        // Packaged Store app: "Publisher.AppName_hash!AppId" → take after last '!'
        int bang = aumid.LastIndexOf('!');
        if (bang >= 0 && bang < aumid.Length - 1)
        {
            string name = aumid[(bang + 1)..];
            // Strip any leading dotted prefix e.g. "Microsoft.ZuneMusic" → "ZuneMusic"
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name[(dot + 1)..];
            return name;
        }
        return aumid;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_trackedSession != null)
        {
            _trackedSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _trackedSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }
        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged       -= OnSessionsChanged;
        }
    }
}

