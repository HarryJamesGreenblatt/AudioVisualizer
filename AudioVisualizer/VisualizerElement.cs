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

    // Always-present entities — bars and peaks are the foundation.
    private readonly BarEntity _bars = new();
    private readonly PeakEntity _peaks;

    // Optional entities — null when their layer is toggled off.
    private RainEntity? _rain;
    private BallEntity? _ball;
    #endregion

    #region Properties
    /// <summary>Expose the scene's transient queue for the audio thread to enqueue events.</summary>
    public EventQueue<TransientEvent> TransientQueue => _scene.TransientQueue;

    /// <summary>Expose the scene for external entity management.</summary>
    public Scene Scene => _scene;

    /// <summary>Whether the rain layer is active.</summary>
    public bool IsRainEnabled => _rain != null;

    /// <summary>Whether the beach ball is active.</summary>
    public bool IsBallEnabled => _ball != null;
    #endregion

    #region Constructor
    public VisualizerElement()
    {
        // Bars and peaks are always present.
        _peaks = new PeakEntity(_bars);
        _scene.Add(_bars);
        _scene.Add(_peaks);

        // Wire the particle pool's bar reference so rain drops can collide with bars.
        if (_scene.Particles.Physics is AudioVisualizer.Engine.Components.PhysicsComponent.Particle pp)
            pp.Bars = _bars.Bars;

        // Start with optional layers enabled.
        SetRain(true);
        SetBall(true);

        // Mouse input bridge: WPF events → Scene.Mouse. The engine never references WPF.
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

    #region Layer Toggles
    /// <summary>Add or remove the peak-hold indicators.</summary>
    /// <summary>Add or remove the rain layer.</summary>
    public void SetRain(bool enabled)
    {
        if (enabled && _rain == null)
        {
            _rain = new RainEntity(_scene.Particles);
            _scene.Add(_rain);
        }
        else if (!enabled && _rain != null)
        {
            _scene.Remove(_rain);
            _rain = null;
        }
    }

    /// <summary>Add or remove the beach ball.</summary>
    public void SetBall(bool enabled)
    {
        if (enabled && _ball == null)
        {
            _ball = new BallEntity(
                position: new Point(200, 100),
                bars: _bars,
                peaks: _peaks,
                radius: 40,
                initialVelocity: new Vector(100, 50));

            // Let rain drops collide with the ball.
            if (_scene.Particles.Physics is AudioVisualizer.Engine.Components.PhysicsComponent.Particle pp)
                pp.BallEntityRef = _ball;

            _scene.Add(_ball);
        }
        else if (!enabled && _ball != null)
        {
            _scene.Remove(_ball);

            // Clear ball collision reference so particle physics doesn't hold a dead ref.
            if (_scene.Particles.Physics is AudioVisualizer.Engine.Components.PhysicsComponent.Particle pp)
                pp.BallEntityRef = null;

            _ball = null;
        }
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
