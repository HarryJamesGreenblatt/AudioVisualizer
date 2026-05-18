using System;
using NAudio.CoreAudioApi;

namespace WavBall.Services;

/// <summary>
/// Read/write the Windows master output volume via the default render endpoint
/// (NAudio's MMDevice + AudioEndpointVolume). The same physical knob Windows
/// surfaces in the taskbar tray flyout.
/// </summary>
internal sealed class SystemVolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDevice _device;
    private readonly AudioEndpointVolumeNotificationDelegate _onChanged;
    private bool _suppressNotify;

    /// <summary>Fired when the system volume changes externally (taskbar, hardware keys, etc.).</summary>
    public event Action<float>? VolumeChangedExternally;

    public SystemVolumeService()
    {
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _onChanged = OnVolumeNotification;
        _device.AudioEndpointVolume.OnVolumeNotification += _onChanged;
    }

    /// <summary>Current master volume scalar in [0, 1].</summary>
    public float Volume
    {
        get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        set
        {
            _suppressNotify = true;
            try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }
            finally { _suppressNotify = false; }
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_suppressNotify) return;
        VolumeChangedExternally?.Invoke(data.MasterVolume);
    }

    public void Dispose()
    {
        try { _device.AudioEndpointVolume.OnVolumeNotification -= _onChanged; } catch { }
        _device.Dispose();
        _enumerator.Dispose();
    }
}
