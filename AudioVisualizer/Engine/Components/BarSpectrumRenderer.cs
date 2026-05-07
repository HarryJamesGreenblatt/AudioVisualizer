using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Render component for the frequency spectrum bars and peak-hold indicators.
/// Reads bar heights from <see cref="BarSpectrumReactive"/> and peak positions from <see cref="PeakHoldPhysics"/>.
/// </summary>
public sealed class BarSpectrumRenderer : IRenderComponent
{
    #region Fields
    /// <summary>
    /// Reference to the audio-reactive component providing current bar heights.
    /// </summary>
    private readonly BarSpectrumReactive _bars;

    /// <summary>
    /// Reference to the physics component providing peak-hold positions.
    /// </summary>
    private readonly PeakHoldPhysics _peaks;

    /// <summary>
    /// Gradient brush used to fill spectrum bars (accent → lighter upward).
    /// </summary>
    private readonly Brush _barBrush;

    /// <summary>
    /// Solid brush used to draw peak-hold indicator lines.
    /// </summary>
    private readonly Brush _peakBrush;
    #endregion

    #region Constructor
    /// <summary>
    /// Create the renderer, reading the Windows accent color for the bar gradient.
    /// </summary>
    /// <param name="bars">Audio-reactive component providing bar heights.</param>
    /// <param name="peaks">Physics component providing peak-hold positions.</param>
    public BarSpectrumRenderer(BarSpectrumReactive bars, PeakHoldPhysics peaks)
    {
        _bars = bars;
        _peaks = peaks;

        var accent = GetWindowsAccentColor();
        var lighter = LightenColor(accent, 0.5f);

        _barBrush = new LinearGradientBrush(accent, lighter, new Point(0, 1), new Point(0, 0));
        _barBrush.Freeze();

        _peakBrush = new SolidColorBrush(Colors.White);
        _peakBrush.Freeze();
    }
    #endregion

    #region Methods
    /// <summary>
    /// Draw spectrum bars and peak-hold indicators to the given drawing context.
    /// </summary>
    /// <param name="entity">The owning entity (unused; layout is viewport-relative).</param>
    /// <param name="dc">WPF drawing context for immediate-mode rendering.</param>
    /// <param name="viewport">Current viewport dimensions for bar sizing.</param>
    public void Render(SceneEntity entity, DrawingContext dc, Size viewport)
    {
        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return;

        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, viewport.Width, viewport.Height));

        double totalWidth = viewport.Width;
        double barWidth = totalWidth / barHeights.Length;
        double gap = Math.Max(1, barWidth * 0.15);
        double drawWidth = barWidth - gap;

        var peakHeights = _peaks.PeakHeights;

        for (int i = 0; i < barHeights.Length; i++)
        {
            double x = i * barWidth;
            double height = Math.Clamp(barHeights[i], 0, viewport.Height);
            double y = viewport.Height - height;

            if (height > 1)
                dc.DrawRectangle(_barBrush, null, new Rect(x, y, drawWidth, height));

            // Peak hold dot
            if (peakHeights.Length > i)
            {
                double peakY = viewport.Height - Math.Clamp(peakHeights[i], 0, viewport.Height);
                dc.DrawRectangle(_peakBrush, null, new Rect(x, peakY, drawWidth, 2));
            }
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Read the Windows accent color from the DWM registry key.
    /// Falls back to a default blue if unavailable.
    /// </summary>
    private static Color GetWindowsAccentColor()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                "AccentColor", null);

            if (value is int abgr)
            {
                byte b = (byte)((abgr >> 16) & 0xFF);
                byte g = (byte)((abgr >> 8) & 0xFF);
                byte r = (byte)(abgr & 0xFF);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch { /* fall through */ }
        return Color.FromRgb(0, 120, 215);
    }

    /// <summary>
    /// Lighten a color by the given factor (0 = unchanged, 1 = white).
    /// </summary>
    /// <param name="c">Source color.</param>
    /// <param name="factor">Lightening factor between 0 and 1.</param>
    private static Color LightenColor(Color c, float factor)
    {
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * factor),
            (byte)(c.G + (255 - c.G) * factor),
            (byte)(c.B + (255 - c.B) * factor));
    }
    #endregion
}
