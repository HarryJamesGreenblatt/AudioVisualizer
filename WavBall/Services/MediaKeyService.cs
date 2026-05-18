using System.Runtime.InteropServices;

namespace WavBall.Services;

/// <summary>
/// Sends Windows media-key virtual keys (Next/Prev/PlayPause/Stop) so any app
/// that listens for them — Spotify, browsers (YouTube/SoundCloud), Groove,
/// foobar2000, etc. — responds as if a hardware media key was pressed.
/// Lighter-weight than SMTC (no WinRT TFM bump); upgrade path is to wrap
/// GlobalSystemMediaTransportControlsSessionManager for real session control.
/// </summary>
internal static class MediaKeyService
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP       = 0xB2;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_KEYUP     = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    private static void Tap(byte vk)
    {
        keybd_event(vk, 0, 0, 0);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
    }

    public static void NextTrack() => Tap(VK_MEDIA_NEXT_TRACK);
    public static void PrevTrack() => Tap(VK_MEDIA_PREV_TRACK);
    public static void PlayPause() => Tap(VK_MEDIA_PLAY_PAUSE);
    public static void Stop()      => Tap(VK_MEDIA_STOP);
}
