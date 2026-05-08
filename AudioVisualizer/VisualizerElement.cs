using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AudioVisualizer.Engine;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer;

/// <summary>
/// WPF host element for the Scene engine. Owns the Scene, constructs the initial
/// entity set (bars, peaks, beach ball), and forwards frame ticks from the compositor.
///
/// All physics/render/reactive concerns live inside the entities themselves —
/// this class is purely a WPF↔engine bridge (frame ticks + mouse input).
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
        var rain = new RainEntity(_scene.Particles);
        var ball = new BallEntity(
            position: new Point(200, 100),
            bars: bars,
            peaks: peaks,
            radius: 40,
            initialVelocity: new Vector(100, 50));

        // Wire the particle pool's collision physics to the bar surface so rain drops
        // can bounce off the visible spectrum (and the floor) instead of falling forever,
        // and to the ball so drops splash off it too.
        if (_scene.Particles.Physics is AudioVisualizer.Engine.Components.PhysicsComponent.Particle pp)
        {
            pp.Bars = bars.Bars;
            pp.BallEntityRef = ball;
        }

        // Render order: bars (background) → rain (mid) → peaks → ball (foreground).
        _scene.Add(bars);
        _scene.Add(rain);
        _scene.Add(peaks);
        _scene.Add(ball);

        // Mouse input bridge: WPF events → Scene.Mouse. The engine never references WPF.
        // The viewport is fully painted each frame by the bar renderer, so hit-testing
        // already covers every pixel in our bounds.
        Focusable = true;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp   += OnMouseUp;
        MouseLeave += OnMouseLeave;
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

    #region Mouse → Scene.Mouse bridge
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(this);
        var m = _scene.Mouse;
        m.Position = p;
        m.IsDown = true;
        m.JustPressed = true;
        m.JustReleased = false;
        _scene.Mouse = m;
        CaptureMouse(); // keep receiving moves even if the cursor leaves our bounds during a drag
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var m = _scene.Mouse;
        m.Position = e.GetPosition(this);
        _scene.Mouse = m;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var m = _scene.Mouse;
        m.Position = e.GetPosition(this);
        m.IsDown = false;
        m.JustPressed = false;
        m.JustReleased = true;
        _scene.Mouse = m;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        // Treat losing the cursor mid-drag the same as a release so the ball doesn't
        // get stuck in kinematic mode if the window loses focus or the cursor exits.
        if (_scene.Mouse.IsDown)
        {
            var m = _scene.Mouse;
            m.IsDown = false;
            m.JustReleased = true;
            _scene.Mouse = m;
        }
    }
    #endregion
}
