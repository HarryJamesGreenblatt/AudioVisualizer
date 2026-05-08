using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components.Reactivity;
using Microsoft.Win32;

namespace AudioVisualizer.Engine.Components.Rendering;

/// <summary>
/// Render component for the frequency spectrum bars.
/// Reads heights from a sibling BarReactivity component on the same entity.
/// Also clears the viewport with black before drawing (acts as the scene background).
/// </summary>
public sealed class BarRenderer : IRenderingComponent
{
    #region Fields
    /// <summary>
    /// Reference to the bar reactivity providing current bar heights.
    /// </summary>
    private readonly BarReactivity _bars;

    /// <summary>
    /// Gradient brush used to fill spectrum bars (accent → lighter upward).
    /// </summary>
    private readonly Brush _barBrush;
    #endregion

    #region Constructor
    /// <summary>
    /// Create the renderer, reading the Windows accent color for the bar gradient.
    /// </summary>
    /// <param name="bars">Reactivity component providing live bar heights.</param>
    public BarRenderer(BarReactivity bars)
    {
        _bars = bars;

        var accent = GetWindowsAccentColor();
        var lighter = LightenColor(accent, 0.5f);

        _barBrush = new LinearGradientBrush(accent, lighter, new Point(0, 1), new Point(0, 0));
        _barBrush.Freeze();
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void Render(SceneEntity entity, DrawingContext dc, Size viewport)
    {
        // Clear background — bars are the bottom render layer
        dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, viewport.Width, viewport.Height));

        var barHeights = _bars.BarHeights;
        if (barHeights.Length == 0) return;

        double totalWidth = viewport.Width;
        double barWidth = totalWidth / barHeights.Length;
        double gap = Math.Max(1, barWidth * 0.15);
        double drawWidth = barWidth - gap;

        for (int i = 0; i < barHeights.Length; i++)
        {
            double x = i * barWidth;
            double height = Math.Clamp(barHeights[i], 0, viewport.Height);
            double y = viewport.Height - height;

            if (height > 1)
                dc.DrawRectangle(_barBrush, null, new Rect(x, y, drawWidth, height));
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
    private static Color LightenColor(Color c, float factor)
    {
        return Color.FromRgb(
            (byte)(c.R + (255 - c.R) * factor),
            (byte)(c.G + (255 - c.G) * factor),
            (byte)(c.B + (255 - c.B) * factor));
    }
    #endregion
}
