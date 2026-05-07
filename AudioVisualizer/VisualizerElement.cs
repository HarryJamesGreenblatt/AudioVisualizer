using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine;
using AudioVisualizer.Engine.Components;

namespace AudioVisualizer;

/// <summary>
/// WPF host element for the Scene engine.
/// Owns the Scene, the bar-spectrum entity, and the render surface.
/// MainWindow feeds it audio data and frame ticks; it delegates to the ECS.
/// </summary>
public sealed class VisualizerElement : FrameworkElement
{
    private readonly Scene _scene = new();
    private readonly SceneEntity _barEntity;
    private readonly BarSpectrumReactive _barReactive;
    private DateTime _lastTick = DateTime.UtcNow;

    public VisualizerElement()
    {
        // Wire up the bar-spectrum entity with its three components
        _barReactive = new BarSpectrumReactive();
        var peakPhysics = new PeakHoldPhysics(_barReactive);
        var renderer = new BarSpectrumRenderer(_barReactive, peakPhysics);

        _barEntity = new SceneEntity
        {
            AudioReactive = _barReactive,
            Physics = peakPhysics,
            Render = renderer,
        };

        _scene.Add(_barEntity);
    }

    /// <summary>Expose the scene's transient queue for the audio thread to enqueue events.</summary>
    public EventQueue<TransientEvent> TransientQueue => _scene.TransientQueue;

    /// <summary>Expose the scene for external entity management (e.g., adding beach balls).</summary>
    public Scene Scene => _scene;

    /// <summary>
    /// Called each frame by the compositor (CompositionTarget.Rendering).
    /// Feeds audio data into the scene and advances physics, regardless of whether new audio arrived.
    /// </summary>
    public void Tick(ReadOnlySpan<float> bands)
    {
        var now = DateTime.UtcNow;
        float dt = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Store viewport height on entity position so audio-reactive can scale into pixels
        _barEntity.Position = new Point(ActualWidth, ActualHeight);

        _scene.Tick(dt, bands, new Size(ActualWidth, ActualHeight));
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        _scene.Render(dc, new Size(ActualWidth, ActualHeight));
    }
}
