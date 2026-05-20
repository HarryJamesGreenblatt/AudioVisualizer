using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WavBall.Components;
using WavBall.Configuration;
using WavBall.Entities;
using WavBall.Models;
using WavBall.Services;

namespace WavBall;

/// <summary>
/// WPF host element for the Scene engine. Owns the Scene, constructs the initial
/// entity set (bars, peaks, beach ball), and forwards frame ticks from the compositor.
///
/// All physics/render/reactive concerns live inside the entities themselves —
/// this class is purely a WPF↔engine bridge (frame ticks + mouse input + game mode).
/// </summary>
public sealed class VisualizerElement : FrameworkElement
{
    #region Fields
    private readonly Scene _scene = new();
    private DateTime _lastTick = DateTime.UtcNow;

    // Always-present entities — bars and peaks are the foundation.
    private readonly Bar _bars = new();
    private readonly Peak _peaks;

    // Optional entities — null when their layer is toggled off.
    private Rain? _rain;
    private Ball? _ball;

    // Game mode state — always on by default.
    private bool _gameModeEnabled = true;
    private int _currentStage;
    private Goal? _goal;
    private float _respawnTimer;
    private const float RespawnDelay = 0.75f; // seconds between vaporize and next ball spawn

    /// <summary>
    /// Anti-cheat: true while the goal is suppressed (invisible + untriggerable + frozen).
    /// Set when the user grabs the ball; cleared only after the ball is released AND
    /// has made contact with the bar/peak surface, preventing drop-in cheats.
    /// </summary>
    private bool _goalSuppressed;

    /// <summary>Round-timer service: starts on ball spawn, stops on goal hit.</summary>
    private readonly RoundTimerService _timer = new();

    /// <summary>True while audio capture is active — controls whether ball spawn starts the timer.</summary>
    private bool _audioRunning;
    #endregion

    #region Properties
    /// <summary>Expose the scene's transient queue for the audio thread to enqueue events.</summary>
    public EventQueue<TransientEvent> TransientQueue => _scene.TransientQueue;

    /// <summary>Expose the scene for external entity management.</summary>
    public Scene Scene => _scene;

    /// <summary>Whether the rain layer is active.</summary>
    public bool IsRainEnabled => _rain != null;

    /// <summary>Whether the ball is active.</summary>
    public bool IsBallEnabled => _ball != null;

    /// <summary>Whether game mode (goal + stages) is active.</summary>
    public bool IsGameModeEnabled => _gameModeEnabled;

    /// <summary>Current stage index (0-based). Only meaningful when game mode is on.</summary>
    public int CurrentStage => _currentStage;

    /// <summary>Name of the current ball type.</summary>
    public string CurrentBallName => _currentStage < BallPreset.Stages.Length
        ? BallPreset.Stages[_currentStage].Name
        : "???";

    /// <summary>Pre-formatted LED-style readout for the round timer (e.g. "▶ 00:14.27").</summary>
    public string LedText => _timer.GetReadout();

    /// <summary>Whether the goal is currently collidable.</summary>
    public bool IsGoalCollidable => _goal?.IsCollidable ?? false;

    /// <summary>Current goal mood ("Feeding" or "Sated"), or empty if no goal.</summary>
    public string GoalMood => _goal?.CurrentMood ?? "";

    /// <summary>Suspend timing when audio capture stops or pauses.</summary>
    public void PauseRoundTimer()
    {
        _audioRunning = false;
        _timer.Pause();
    }

    /// <summary>
    /// Begin or resume timing when audio capture starts.
    /// • Paused  → resumes from the saved elapsed value.
    /// • Idle / stopped → starts a fresh count for the current ball (new round).
    /// </summary>
    public void ResumeRoundTimer()
    {
        _audioRunning = true;
        if (_ball == null) return;
        if (_timer.IsPaused)
            _timer.Resume();
        else if (!_timer.IsRunning)
            _timer.Start();
    }

    /// <summary>
    /// Fired on the UI thread when a ball reaches the goal.
    /// Carries: ball kind, ball name, and the final elapsed round time.
    /// </summary>
    public event Action<BallKind, string, TimeSpan>? RoundCompleted;
    #endregion

    #region Constructor
    public VisualizerElement()
    {
        // Bars and peaks are always present.
        _peaks = new Peak(_bars);
        _scene.Add(_bars);
        _scene.Add(_peaks);

        // Wire the particle pool's bar and peak references so rain drops can collide with both.
        if (_scene.Particles.Physics is WavBall.Components.Physics.Particle pp)
        {
            pp.Bars = _bars.Bars;
            pp.PeaksRef = _peaks.Physics as WavBall.Components.Physics.Peak;
        }

        // Start with rain off, ball + game mode on by default.
        SetBall(true);
        SpawnGoal();

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

        // Game mode respawn timer: after vaporizing a ball, wait before spawning the next.
        if (_gameModeEnabled && _respawnTimer > 0 && _ball == null)
        {
            _respawnTimer -= dt;
            if (_respawnTimer <= 0)
                SpawnNextStageBall();
        }

        _scene.Tick(dt, bands, new Size(ActualWidth, ActualHeight));

        // Anti-cheat: suppress goal while user is dragging, re-enable after release + surface contact.
        if (_gameModeEnabled && _goal != null && _ball != null)
        {
            var ballPhysics = _ball.Physics as Physics.Ball;
            if (_ball.IsKinematic)
            {
                // User is holding the ball — suppress goal immediately
                if (!_goalSuppressed)
                {
                    _goalSuppressed = true;
                    _goal.Enabled = false;
                }
            }
            else if (_goalSuppressed && ballPhysics != null && ballPhysics.HasSurfaceContact)
            {
                // Ball has been released AND has touched the bar/peak surface — re-enable goal
                _goalSuppressed = false;
                _goal.Enabled = true;
            }
        }

        // NOTE: goal motion lives in Steering.Goal (an autonomous-agent component on the
        // Goal entity), driven through the normal World.Update pipeline. Nothing to do here.

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
    /// <summary>Add or remove the rain layer.</summary>
    public void SetRain(bool enabled)
    {
        if (enabled && _rain == null)
        {
            _rain = new Rain(_scene.Particles, _bars.Bars);
            _scene.Add(_rain);
        }
        else if (!enabled && _rain != null)
        {
            _scene.Remove(_rain);
            _rain = null;
        }
    }

    /// <summary>Add or remove the ball (uses current stage preset when game mode is active).</summary>
    public void SetBall(bool enabled)
    {
        if (enabled && _ball == null)
        {
            var preset = _gameModeEnabled && _currentStage < BallPreset.Stages.Length
                ? BallPreset.Stages[_currentStage]
                : BallPreset.Stages[0]; // default to beach ball outside game mode

            _ball = new Ball(
                position: new Point(ActualWidth > 0 ? ActualWidth / 2 : 200, 100),
                bars: _bars,
                peaks: _peaks,
                preset: preset,
                initialVelocity: new Vector(100, 50));

            // Let rain drops collide with the ball.
            if (_scene.Particles.Physics is WavBall.Components.Physics.Particle pp)
                pp.BallEntityRef = _ball;

            _scene.Add(_ball);

            // Start timing immediately if audio is already playing; otherwise show READY
            // and wait for the user to hit Play.
            if (_audioRunning) _timer.Start();
        }
        else if (!enabled && _ball != null)
        {
            _scene.Remove(_ball);

            // Clear ball collision reference so particle physics doesn't hold a dead ref.
            if (_scene.Particles.Physics is WavBall.Components.Physics.Particle pp)
                pp.BallEntityRef = null;

            // Clear ball ref on goal renderer so it stops tracking the dead ball.
            _goal?.SetBallRef(null);

            _ball = null;

            // Ball toggled off without a goal hit — wipe readout back to idle.
            _timer.Reset();
        }
    }
    #endregion

    #region Game Mode
    /// <summary>Enable or disable game mode (goal + stage progression).</summary>
    public void SetGameMode(bool enabled)
    {
        if (enabled == _gameModeEnabled) return;
        _gameModeEnabled = enabled;

        if (enabled)
        {
            _currentStage = 0;
            _respawnTimer = 0;

            // Ensure ball is active with stage 0 preset
            if (_ball != null) SetBall(false);
            SetBall(true);

            // Spawn the first goal
            SpawnGoal();
        }
        else
        {
            // Remove goal
            if (_goal != null)
            {
                _scene.Remove(_goal);
                _goal = null;
            }

            // Reset to default beach ball
            if (_ball != null) SetBall(false);
            _currentStage = 0;
            SetBall(true);
        }
    }

    /// <summary>
    /// Compute goal position for a given stage. Early stages are lower and more
    /// centered; later stages are higher and offset toward weaker bar regions.
    /// X is the stage's nominal position — the runtime <see cref="UpdateGoalAudioDrift"/>
    /// pass then drifts the goal toward the heat centroid each tick, so static placement
    /// is just an anchor, not a final destination.
    /// </summary>
    private Point GoalPositionForStage(int stage)
    {
        double w = ActualWidth > 0 ? ActualWidth : 800;
        double h = ActualHeight > 0 ? ActualHeight : 400;

        // Vertical: starts at 55% height, rises ~6% per stage, capped at 15% from top
        double yFrac = Math.Max(0.15, 0.55 - stage * 0.06);

        // Horizontal: oscillates between left-center and right-center
        double xFrac = stage % 2 == 0 ? 0.65 : 0.35;

        return new Point(w * xFrac, h * yFrac);
    }

    /// <summary>Spawn a goal entity for the current stage, watching the current ball.</summary>
    private void SpawnGoal()
    {
        if (_goal != null)
        {
            _scene.Remove(_goal);
            _goal = null;
        }

        if (_ball == null) return;

        // Goal diameter matches ball diameter exactly
        var preset = BallPreset.Stages[_currentStage];
        double goalRadius = preset.Radius;
        var pos = GoalPositionForStage(_currentStage);

        // Wire the goal with the spectrum so its Steering.Goal component can pick targets
        // and its Rendering.Goal can pulse with the music.
        _goal = new Goal(pos, goalRadius, _ball, _bars.Bars);
        _goal.Collision += OnGoalHit;
        _goalSuppressed = false; // new goal starts enabled
        _scene.Add(_goal);

        // Wire gravitational attraction: ball physics pulls toward goal.
        if (_ball?.Physics is Physics.Ball ballPhys)
            ballPhys.GoalEntityRef = _goal;
    }

    /// <summary>Handle goal collision: vaporize current ball, advance stage, start respawn timer.</summary>
    private void OnGoalHit(World goal, CollisionInfo info)
    {
        if (_ball == null) return;

        // Freeze the round timer at the moment of capture.
        _timer.Stop();

        // Notify listeners — snapshot stage BEFORE it increments below.
        RoundCompleted?.Invoke(
            BallPreset.Stages[_currentStage].Kind,
            BallPreset.Stages[_currentStage].Name,
            _timer.Elapsed);

        // Vaporize: particle burst from ball position with colors matching the ball
        var ballPos = _ball.Position;
        var ballVel = _ball.Velocity;
        var colors = GetBallColors(_currentStage);
        int burstCount = 120;
        var rng = new Random();

        for (int i = 0; i < burstCount; i++)
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double speed = 100 + rng.NextDouble() * 300;
            var vel = new Vector(Math.Cos(angle) * speed, Math.Sin(angle) * speed) + ballVel * 0.3;
            var color = colors[rng.Next(colors.Length)];
            float size = 0.5f + (float)rng.NextDouble() * 1.0f;
            _scene.Particles.Spawn(ballPos, vel, lifetimeFrames: 40 + rng.Next(30), color, size: size);
        }

        // Mark entities dead — they'll be reaped by Scene.Tick's end-of-frame RemoveAll.
        // We CAN'T call Scene.Remove here because this fires mid-iteration during
        // ResolveCollisions, and mutating the entity list during foreach is undefined.
        _ball.IsAlive = false;

        // Clear ball collision reference so particle physics doesn't hold a dead ref.
        if (_scene.Particles.Physics is WavBall.Components.Physics.Particle pp)
            pp.BallEntityRef = null;
        _ball = null;

        if (_goal != null)
        {
            _goal.IsAlive = false;
            _goal = null;
        }

        // Advance stage
        _currentStage++;
        if (_currentStage >= BallPreset.Stages.Length)
            _currentStage = 0; // wrap around (victory lap)

        // Start respawn timer
        _respawnTimer = RespawnDelay;
    }

    /// <summary>Spawn the next stage ball and goal after the respawn delay.</summary>
    private void SpawnNextStageBall()
    {
        SetBall(true);
        SpawnGoal();
    }

    /// <summary>Get representative colors for a ball type (used for vaporize particle burst).</summary>
    private static Color[] GetBallColors(int stage)
    {
        var preset = stage < BallPreset.Stages.Length ? BallPreset.Stages[stage] : BallPreset.Stages[0];
        return preset.Kind switch
        {
            BallKind.BeachBall     => [Color.FromRgb(220, 50, 50), Color.FromRgb(255, 220, 50), Color.FromRgb(50, 120, 220), Colors.White],
            BallKind.SuperBall     => [Color.FromRgb(30, 140, 255), Color.FromRgb(60, 200, 255), Color.FromRgb(20, 100, 200)],
            BallKind.PingPongBall  => [Colors.White, Color.FromRgb(255, 200, 140), Color.FromRgb(255, 240, 220)],
            BallKind.Racquetball   => [Color.FromRgb(30, 100, 220), Color.FromRgb(50, 130, 255), Color.FromRgb(20, 70, 180)],
            BallKind.WiffleBall    => [Colors.White, Color.FromRgb(255, 255, 200), Color.FromRgb(240, 240, 230)],
            BallKind.Volleyball    => [Colors.White, Color.FromRgb(255, 220, 50), Color.FromRgb(50, 100, 200)],
            BallKind.TennisBall    => [Color.FromRgb(200, 220, 50), Color.FromRgb(180, 200, 40), Colors.White],
            BallKind.Handball      => [Color.FromRgb(180, 40, 40), Color.FromRgb(220, 100, 30), Color.FromRgb(30, 30, 30)],
            BallKind.LacrosseBall  => [Colors.White, Color.FromRgb(220, 220, 220), Color.FromRgb(180, 180, 180)],
            BallKind.SoccerBall    => [Colors.White, Color.FromRgb(200, 200, 200), Color.FromRgb(30, 30, 30)],
            BallKind.Basketball    => [Color.FromRgb(200, 100, 20), Color.FromRgb(230, 130, 40), Color.FromRgb(160, 80, 15)],
            BallKind.WaterPoloBall => [Color.FromRgb(255, 220, 50), Color.FromRgb(50, 120, 200), Colors.White],
            BallKind.Football      => [Color.FromRgb(140, 80, 30), Color.FromRgb(180, 110, 50), Colors.White],
            BallKind.GolfBall      => [Colors.White, Color.FromRgb(245, 245, 240), Color.FromRgb(220, 220, 210)],
            BallKind.Dodgeball     => [Color.FromRgb(200, 30, 30), Color.FromRgb(150, 20, 20), Color.FromRgb(30, 30, 30)],
            BallKind.BilliardBall  => [Color.FromRgb(160, 20, 20), Color.FromRgb(240, 230, 200), Color.FromRgb(200, 170, 50)],
            BallKind.Baseball      => [Color.FromRgb(245, 240, 230), Color.FromRgb(200, 40, 40), Colors.White],
            BallKind.CricketBall   => [Color.FromRgb(160, 30, 30), Color.FromRgb(120, 15, 15), Color.FromRgb(200, 170, 50)],
            BallKind.SquashBall    => [Color.FromRgb(20, 20, 30), Color.FromRgb(40, 40, 60), Color.FromRgb(10, 10, 15)],
            BallKind.MedicineBall  => [Color.FromRgb(30, 30, 30), Color.FromRgb(140, 30, 30), Color.FromRgb(80, 80, 80)],
            BallKind.BowlingBall   => [Color.FromRgb(40, 40, 55), Color.FromRgb(60, 60, 80), Color.FromRgb(25, 25, 35)],
            BallKind.BocceBall     => [Color.FromRgb(40, 120, 50), Color.FromRgb(200, 170, 50), Color.FromRgb(240, 230, 200)],
            BallKind.Cannonball    => [Color.FromRgb(60, 60, 60), Color.FromRgb(100, 70, 40), Color.FromRgb(30, 30, 30)],
            _                      => [Colors.White],
        };
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
