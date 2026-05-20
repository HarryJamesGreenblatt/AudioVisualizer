namespace WavBall.Configuration;

/// <summary>
/// Identifies the ball type for rendering dispatch and game-mode progression.
/// </summary>
public enum BallKind
{
    BeachBall,
    SuperBall,
    PingPongBall,
    Racquetball,
    WiffleBall,
    Volleyball,
    TennisBall,
    Handball,
    LacrosseBall,
    SoccerBall,
    Basketball,
    WaterPoloBall,
    Football,
    GolfBall,
    Dodgeball,
    BilliardBall,
    Baseball,
    CricketBall,
    SquashBall,
    MedicineBall,
    BowlingBall,
    BocceBall,
    Cannonball,
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
    ///   Super ball   ~0.92  (solid rubber, maximum elasticity)
    ///   Ping pong    ~0.90  (hollow celluloid/poly, ultra-light)
    ///   Racquetball  ~0.87  (solid rubber, extremely bouncy — researched 0.85–0.90)
    ///   Wiffle ball  ~0.40  (perforated hollow plastic, wind-dominated)
    ///   Volleyball   ~0.75  (inflated leather/synthetic, similar to basketball)
    ///   Tennis ball  ~0.73  (ITF regulation: 0.728–0.775 on rigid surface)
    ///   Handball     ~0.68  (resin-coated leather, grippy)
    ///   Lacrosse     ~0.70  (solid vulcanized rubber)
    ///   Soccer ball  ~0.80  (FIFA tests on hard surface)
    ///   Basketball   ~0.76  (FIBA regulation: 0.758–0.776)
    ///   Water polo   ~0.65  (waterproof rubber, grippy)
    ///   Football     ~0.62  (prolate spheroid leather; COR for short-axis bounce)
    ///   Golf ball    ~0.83  (solid core, urethane cover, dimpled)
    ///   Dodgeball    ~0.55  (foam-filled rubber, spongy)
    ///   Billiard     ~0.58  (phenolic resin, hard but energy-absorbing cushion hits)
    ///   Baseball     ~0.55  (MLB dead ball: cork/rubber core, leather cover)
    ///   Cricket ball ~0.45  (cork core, leather cover, harder than baseball)
    ///   Squash ball  ~0.25  (hollow rubber, designed to be dead)
    ///   Medicine ball~0.20  (heavy vinyl/leather, sand-filled)
    ///   Bowling ball ~0.15  (polyurethane/urethane; physically ~0.60, but lowered for game feel)
    ///   Bocce ball   ~0.12  (solid resin/metal, heavy roller)
    ///   Cannonball   ~0.08  (cast iron, the final boss)
    /// </summary>
    public static readonly BallPreset[] Stages =
    [
        //                Kind                     Name                Radius  Mass   Grav   Rest   LinD    QuadD    AngD   Fric   Roll
        new(BallKind.BeachBall,      "Beach Ball",       40,     1.0,   800f,  0.35f, 1.50f, 0.0160f, 1.2f,  0.55f, 1.5f),
        new(BallKind.SuperBall,      "Super Ball",       11,     0.5,   800f,  0.92f, 0.08f, 0.0004f, 0.3f,  0.30f, 2.5f),
        new(BallKind.PingPongBall,   "Ping Pong Ball",   10,     0.3,   800f,  0.90f, 0.35f, 0.0030f, 1.0f,  0.25f, 2.8f),
        new(BallKind.Racquetball,    "Racquetball",      14,     0.8,   800f,  0.87f, 0.10f, 0.0006f, 0.4f,  0.40f, 2.2f),
        new(BallKind.WiffleBall,     "Wiffle Ball",      16,     0.4,   800f,  0.40f, 0.40f, 0.0025f, 1.5f,  0.30f, 1.8f),
        new(BallKind.Volleyball,     "Volleyball",       27,     2.5,   800f,  0.75f, 0.55f, 0.0060f, 0.7f,  0.55f, 1.1f),
        new(BallKind.TennisBall,     "Tennis Ball",      16,     1.2,   800f,  0.73f, 0.25f, 0.0015f, 0.9f,  0.65f, 1.8f),
        new(BallKind.Handball,       "Handball",         23,     1.5,   800f,  0.68f, 0.20f, 0.0012f, 0.7f,  0.70f, 1.6f),
        new(BallKind.LacrosseBall,   "Lacrosse Ball",    12,     2.0,   800f,  0.70f, 0.12f, 0.0005f, 0.4f,  0.35f, 1.5f),
        new(BallKind.SoccerBall,     "Soccer Ball",      30,     3.5,   800f,  0.80f, 0.50f, 0.0055f, 0.6f,  0.50f, 1.2f),
        new(BallKind.Basketball,     "Basketball",       32,     5.0,   800f,  0.76f, 0.45f, 0.0050f, 0.5f,  0.50f, 1.0f),
        new(BallKind.WaterPoloBall,  "Water Polo Ball",  22,     4.0,   800f,  0.65f, 0.55f, 0.0065f, 0.5f,  0.60f, 0.9f),
        new(BallKind.Football,       "Football",         18,     4.5,   800f,  0.62f, 0.30f, 0.0025f, 0.6f,  0.55f, 1.4f),
        new(BallKind.GolfBall,       "Golf Ball",         8,     3.0,   800f,  0.83f, 0.08f, 0.0003f, 0.3f,  0.30f, 1.2f),
        new(BallKind.Dodgeball,      "Dodgeball",        22,     4.0,   800f,  0.55f, 0.40f, 0.0045f, 0.6f,  0.50f, 1.0f),
        new(BallKind.BilliardBall,   "Billiard Ball",    13,     5.0,   800f,  0.58f, 0.10f, 0.0004f, 0.2f,  0.20f, 0.6f),
        new(BallKind.Baseball,       "Baseball",         16,     6.0,   800f,  0.55f, 0.15f, 0.0012f, 0.3f,  0.35f, 0.8f),
        new(BallKind.CricketBall,    "Cricket Ball",     12,     6.5,   800f,  0.45f, 0.15f, 0.0012f, 0.3f,  0.40f, 0.7f),
        new(BallKind.SquashBall,     "Squash Ball",      10,     1.5,   800f,  0.25f, 0.15f, 0.0008f, 0.5f,  0.45f, 1.5f),
        new(BallKind.MedicineBall,   "Medicine Ball",    35,    12.0,   800f,  0.20f, 0.30f, 0.0050f, 0.2f,  0.40f, 0.3f),
        new(BallKind.BowlingBall,    "Bowling Ball",     28,    15.0,   800f,  0.15f, 0.25f, 0.0040f, 0.2f,  0.25f, 0.4f),
        new(BallKind.BocceBall,      "Bocce Ball",       27,    14.0,   800f,  0.12f, 0.20f, 0.0035f, 0.15f, 0.20f, 0.3f),
        new(BallKind.Cannonball,     "Cannonball",       24,    20.0,   800f,  0.08f, 0.15f, 0.0030f, 0.10f, 0.15f, 0.2f),
    ];
}
