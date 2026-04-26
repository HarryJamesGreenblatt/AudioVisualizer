using System.Windows;
using System.Windows.Media;

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
        // Cyan-to-purple gradient for bars
        _barBrush = new LinearGradientBrush(
            Color.FromRgb(0, 220, 255),
            Color.FromRgb(180, 0, 255),
            new Point(0, 1), new Point(0, 0));
        _barBrush.Freeze();

        _peakBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        _peakBrush.Freeze();
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
