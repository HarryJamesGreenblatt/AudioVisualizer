using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioVisualizer;

/// <summary>
/// Custom WPF element that renders a frequency-spectrum bar visualizer.
/// Call <see cref="Update"/> from the UI thread to push new band data and trigger a redraw.
/// </summary>
public sealed class VisualizerElement : FrameworkElement
{
    private float[] _bands = [];
    private readonly Brush _barBrush;
    private readonly Brush _peakBrush;
    private float[] _peakHold = [];
    private float[] _peakVelocity = [];
    // Reference max rises instantly to new peaks, decays slowly (0.97/frame @ 60fps ≈ 2s to halve).
    // This means when all bands drop together, the scale stays fixed and bars visually fall to zero.
    private float _globalMax = 0.01f;

    public VisualizerElement()
    {
        var accent = GetWindowsAccentColor();
        var lighter = LightenColor(accent, 0.5f);

        // Gradient: accent color at bar base, lightened accent at top
        _barBrush = new LinearGradientBrush(
            accent,
            lighter,
            new Point(0, 1), new Point(0, 0));
        _barBrush.Freeze();

        _peakBrush = new SolidColorBrush(Colors.White);
        _peakBrush.Freeze();
    }

    /// <summary>
    /// Reads the Windows accent color from the DWM registry key.
    /// The value is stored as ABGR (0xAABBGGRR). Falls back to cyan if unavailable.
    /// </summary>
    private static Color GetWindowsAccentColor()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                "AccentColor",
                null);

            if (value is int abgr)
            {
                // DWM stores as 0xAABBGGRR — extract channels accordingly
                byte a = (byte)((abgr >> 24) & 0xFF);
                byte b = (byte)((abgr >> 16) & 0xFF);
                byte g = (byte)((abgr >> 8)  & 0xFF);
                byte r = (byte)( abgr        & 0xFF);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch { /* fall through to default */ }

        return Color.FromRgb(0, 120, 215); // Windows default blue
    }

    /// <summary>Blends a color toward white by the given factor (0=original, 1=white).</summary>
    private static Color LightenColor(Color c, float factor)
    {
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * factor),
            (byte)(c.G + (255 - c.G) * factor),
            (byte)(c.B + (255 - c.B) * factor));
    }

    /// <summary>
    /// Push a new set of frequency band magnitudes and request a redraw.
    /// Must be called on the UI thread.
    /// </summary>
    public void Update(Span<float> bands)
    {
        if (_bands.Length != bands.Length)
        {
            _bands       = new float[bands.Length];
            _peakHold    = new float[bands.Length];
            _peakVelocity = new float[bands.Length];
        }

        // Update global max: rise instantly to new peaks, decay glacially (0.9995/frame
        // @ 60fps ≈ 23s to halve). This keeps the scale rock-stable during music so
        // frame-to-frame scale variance doesn't cause jitter. Bars fall visually because
        // FftProcessor's smoothed values decay — not because the scale shifts.
        float currentMax = 0.001f;
        foreach (float v in bands) if (v > currentMax) currentMax = v;
        if (currentMax > _globalMax)
            _globalMax = currentMax;          // instant rise
        else
            _globalMax = Math.Max(0.01f, _globalMax * 0.9995f); // very slow decay

        float scale = (float)ActualHeight / _globalMax;

        for (int i = 0; i < bands.Length; i++)
        {
            _bands[i] = bands[i] * scale;

            // Peak hold: rise instantly, fall with gravity
            if (_bands[i] >= _peakHold[i])
            {
                _peakHold[i] = _bands[i];
                _peakVelocity[i] = 0f;
            }
            else
            {
                _peakVelocity[i] += 0.5f;          // gravity
                _peakHold[i] = Math.Max(0f, _peakHold[i] - _peakVelocity[i]);
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bands.Length == 0) return;

        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, ActualWidth, ActualHeight));

        double totalWidth = ActualWidth;
        double barWidth   = totalWidth / _bands.Length;
        double gap        = Math.Max(1, barWidth * 0.15);
        double drawWidth  = barWidth - gap;

        for (int i = 0; i < _bands.Length; i++)
        {
            double x      = i * barWidth;
            double height = Math.Clamp(_bands[i], 0, ActualHeight);
            double y      = ActualHeight - height;

            if (height > 1)
                dc.DrawRectangle(_barBrush, null, new Rect(x, y, drawWidth, height));

            // Peak hold dot
            double peakY = ActualHeight - Math.Clamp(_peakHold[i], 0, ActualHeight);
            dc.DrawRectangle(_peakBrush, null, new Rect(x, peakY, drawWidth, 2));
        }
    }
}
