using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components.Physics;
using AudioVisualizer.Engine.Components.Reactivity;

namespace AudioVisualizer.Engine.Components.Rendering;

/// <summary>
/// Render component for peak-hold indicator bars (the white markers atop spectrum bars).
/// Reads peak positions from the sibling PeakPhysics component on the same entity,
/// and bar layout/width from the referenced BarReactivity.
/// </summary>
public sealed class PeakRenderer : IRenderingComponent
{
    #region Fields
    /// <summary>
    /// Reference to the bar reactivity for layout (column width derived from bar count).
    /// </summary>
    private readonly BarReactivity _bars;

    /// <summary>
    /// Reference to the physics component providing peak heights.
    /// </summary>
    private readonly PeakPhysics _peaks;

    /// <summary>
    /// Solid brush used to draw peak-hold indicator lines.
    /// </summary>
    private readonly Brush _peakBrush;
    #endregion

    #region Constructor
    /// <summary>
    /// Create the peak renderer with references to live bar layout and peak data.
    /// </summary>
    /// <param name="bars">Bar reactivity providing layout/band count.</param>
    /// <param name="peaks">Physics component providing peak heights.</param>
    public PeakRenderer(BarReactivity bars, PeakPhysics peaks)
    {
        _bars = bars;
        _peaks = peaks;

        _peakBrush = new SolidColorBrush(Colors.White);
        _peakBrush.Freeze();
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void Render(SceneEntity entity, DrawingContext dc, Size viewport)
    {
        var barHeights = _bars.BarHeights;
        var peakHeights = _peaks.PeakHeights;
        if (barHeights.Length == 0 || peakHeights.Length == 0) return;

        double barWidth = viewport.Width / barHeights.Length;
        double gap = Math.Max(1, barWidth * 0.15);
        double drawWidth = barWidth - gap;

        for (int i = 0; i < peakHeights.Length; i++)
        {
            double x = i * barWidth;
            double peakY = viewport.Height - Math.Clamp(peakHeights[i], 0, viewport.Height);
            dc.DrawRectangle(_peakBrush, null, new Rect(x, peakY, drawWidth, 2));
        }
    }
    #endregion
}
