namespace AudioVisualizer.Configuration;

/// <summary>
/// Identifies the ball type for rendering dispatch and game-mode progression.
/// </summary>
public enum BallKind
{
    BeachBall,
    Racquetball,
    TennisBall,
    SoccerBall,
    Basketball,
    Baseball,
    BowlingBall,
}

/// <summary>
/// Immutable bundle of physics constants and identity for a ball type.
/// Each stage of the game mode uses a different preset from <see cref="Stages"/>.
/// Physics constants are tuned for game feel, not real-world simulation.
/// </summary>
/// <param name="Kind">Ball type identifier — drives rendering and identity.</param>
/// <param name="Name">Human-readable name for UI display.</param>
/// <param name="Radius">Collision/render radius in pixels.</param>
/// <param name="Mass">Inertial mass (arbitrary units). Affects drag deceleration and throw impulse.</param>
/// <param name="Gravity">Gravitational acceleration in px/s².</param>
/// <param name="Restitution">Coefficient of restitution (0 = dead, 1 = perfectly elastic).</param>
/// <param name="LinearDrag">Viscous drag coefficient (1/s). F = −c·v (NOT mass-scaled).</param>
/// <param name="QuadraticDrag">Form drag coefficient (1/px). F = −c·|v|·v (NOT mass-scaled).</param>
/// <param name="AngularDrag">Spin decay rate (1/s). Applied as exponential decay on angular velocity.</param>
/// <param name="FloorFriction">Fraction of horizontal velocity transferred into spin on bounce (0–1).</param>
/// <param name="RollCoupling">How strongly horizontal motion induces rolling spin. Larger = spinnier.</param>
public sealed record BallPreset(
    BallKind Kind,
    string Name,
    double Radius,
    double Mass,
    float Gravity,
    float Restitution,
    float LinearDrag,
    float QuadraticDrag,
    float AngularDrag,
    float FloorFriction,
    float RollCoupling)
{
    /// <summary>
    /// Ordered stage progression. Index 0 is the starting ball; each stage clear advances
    /// to the next preset. COR values are based on real-world research:
    ///   Beach ball   ~0.35  (thin plastic, heavy deformation, absorbs energy)
    ///   Racquetball  ~0.87  (solid rubber, extremely bouncy — researched 0.85–0.90)
    ///   Tennis ball  ~0.73  (ITF regulation: 0.728–0.775 on rigid surface)
    ///   Soccer ball  ~0.80  (FIFA tests on hard surface)
    ///   Basketball   ~0.76  (FIBA regulation: 0.758–0.776)
    ///   Baseball     ~0.55  (MLB dead ball: cork/rubber core, leather cover)
    ///   Bowling ball ~0.15  (polyurethane/urethane; physically ~0.60, but lowered for game feel)
    /// Drag values tuned so terminal velocity reflects aerodynamics:
    ///   Beach ball: floaty (huge cross-section, negligible mass) → v_t ≈ 200 px/s
    ///   Baseball:   cuts through air (small, dense) → v_t ≈ 900 px/s
    ///   Bowling:    unstoppable (dense, moderate area) → v_t ≈ 1400 px/s
    /// </summary>
    public static readonly BallPreset[] Stages =
    [
        //                Kind                Name            Radius  Mass   Grav   Rest   LinD    QuadD    AngD   Fric   Roll
        new(BallKind.BeachBall,   "Beach Ball",    40,     1.0,   800f,  0.35f, 1.50f, 0.0160f, 1.2f,  0.55f, 1.5f),
        new(BallKind.Racquetball, "Racquetball",   14,     0.8,   800f,  0.87f, 0.10f, 0.0006f, 0.4f,  0.40f, 2.2f),
        new(BallKind.TennisBall,  "Tennis Ball",   16,     1.2,   800f,  0.73f, 0.25f, 0.0015f, 0.9f,  0.65f, 1.8f),
        new(BallKind.SoccerBall,  "Soccer Ball",   30,     3.5,   800f,  0.80f, 0.50f, 0.0055f, 0.6f,  0.50f, 1.2f),
        new(BallKind.Basketball,  "Basketball",    32,     5.0,   800f,  0.76f, 0.45f, 0.0050f, 0.5f,  0.50f, 1.0f),
        new(BallKind.Baseball,    "Baseball",      16,     6.0,   800f,  0.55f, 0.15f, 0.0012f, 0.3f,  0.35f, 0.8f),
        new(BallKind.BowlingBall, "Bowling Ball",  28,    15.0,   800f,  0.15f, 0.25f, 0.0040f, 0.2f,  0.25f, 0.4f),
    ];
}
