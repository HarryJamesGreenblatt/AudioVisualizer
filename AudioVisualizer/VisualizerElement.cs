using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer;

/// <summary>
/// WPF host element for the Scene engine. Owns the Scene, constructs the initial
/// entity set (bars, peaks, beach ball), and forwards frame ticks from the compositor.
///
/// All physics/render/reactive concerns live inside the entities themselves —
/// this class is purely a WPF↔engine bridge.
/// </summary>
public sealed class VisualizerElement : FrameworkElement
{
    #region Fields
    private readonly Scene _scene = new();
    private DateTime _lastTick = DateTime.UtcNow;
    #endregion

    #region Properties
    /// <summary>Expose the scene's transient queue for the audio thread to enqueue events.</summary>
    public EventQueue<TransientEvent> TransientQueue => _scene.TransientQueue;

    /// <summary>Expose the scene for external entity management.</summary>
    public Scene Scene => _scene;
    #endregion

    #region Constructor
    public VisualizerElement()
    {
        // Bars first so their renderer clears the background before others draw on top.
        var bars = new BarEntity();
        var peaks = new PeakEntity(bars);
        var ball = new BallEntity(
            position: new Point(200, 100),
            bars: bars,
            peaks: peaks,
            radius: 40,
            initialVelocity: new Vector(100, 50));

        _scene.Add(bars);
        _scene.Add(peaks);
        _scene.Add(ball);
    }
    #endregion

    #region Methods
    /// <summary>
    /// Called each frame by the compositor (CompositionTarget.Rendering).
    /// Feeds audio data into the scene and advances physics regardless of whether new audio arrived.
    /// </summary>
    public void Tick(ReadOnlySpan<float> bands)
    {
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;

        _scene.Tick(dt, bands, new Size(ActualWidth, ActualHeight));
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _scene.Render(dc, new Size(ActualWidth, ActualHeight));
    }
    #endregion
}
