using System;
using System.Windows;

namespace WavBall.Components;

/// <summary>
/// Abstract base for "engagement" / sensor accumulators — components whose job is to
/// look at the world around the owning entity and integrate a single scalar 0–1
/// value reflecting <i>how engaged it currently is with its environment</i>. Distinct
/// from <see cref="Physics"/> (which moves the entity in response to external forces),
/// <see cref="Steering"/> (which moves it according to its own intent), and
/// <see cref="Reactivity"/> (which translates audio bands into per-entity geometry):
/// charge is a slow, position-aware capacitor that other components <i>read</i> to
/// gate their own behavior.
///
/// The first inhabitant is <see cref="Goal"/> — a Gaussian spatial-influence sensor
/// that fills when the goal sits on a loud spectral region and drains otherwise,
/// driving the appetite-machine transitions in <see cref="Steering.Goal"/>, the
/// trigger gate in <see cref="Physics.Goal"/>, and the glow envelope in
/// <see cref="Rendering.Goal"/>. Concrete behaviors are nested types so the entire
/// charge-component surface lives in one file, and the type system enforces "X is
/// a kind of charge sensor" via <c>Charge.X</c>.
///
/// <para>Per-tick pipeline order in <see cref="World.Update"/>:
/// Input → Reactivity → <b>Charge</b> → Steering → Physics. Charge ticks after
/// reactivity (so spectral data is fresh) and before steering (so steering sees
/// this-tick's charge value when deciding what to do).</para>
/// </summary>
public abstract class Charge
{
    #region Properties
    /// <summary>
    /// When false, the sensor is suspended — <see cref="Update"/> becomes a no-op
    /// and the integrated value is held frozen. Used for anti-cheat / pause /
    /// state-machine gating without removing the component.
    /// </summary>
    public bool Enabled { get; set; } = true;
    #endregion

    #region Pipeline
    /// <summary>
    /// Advance the sensor for one fixed-timestep tick. Override to sample the
    /// environment (typically reading from a <see cref="Reactivity"/> sibling and
    /// the owning entity's position) and integrate the internal scalar. Default
    /// no-op so subclasses opt in.
    /// </summary>
    /// <param name="entity">The owning entity whose state/position may be sampled.</param>
    /// <param name="viewport">Current viewport dimensions for pixel-space scaling.</param>
    /// <param name="dt">Fixed physics timestep in seconds.</param>
    public virtual void Update(World entity, Size viewport, float dt) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Goal
    /// <summary>
    /// Charge sensor for the Goal entity. Models the goal as a mosquito with a
    /// metabolism: a Gaussian spatial-influence kernel measures how strongly the
    /// goal is "feeding" from the spectrum at its current position, and an
    /// asymmetric-τ capacitor integrates that signal into a 0–1 charge value.
    ///
    /// <para><b>Field model.</b> Each band <i>i</i> emits a Gaussian "feeding zone"
    /// of width σ centered at its hover point <c>(barX, top - hoverOffset)</c>:
    /// <code>
    ///     φ_i(p) = q_i · exp(− r_i² / 2σ²)
    /// </code>
    /// where <c>q_i = barHeights[i]</c> and <c>r_i</c> is the 2D distance from the
    /// goal to band <i>i</i>'s hover point. The goal samples <c>max_i φ_i</c> at its
    /// current position — the mosquito feeds from the single spot it's on, not from
    /// the spectral integral.</para>
    ///
    /// <para><b>Why Gaussian, not Coulomb 1/r.</b> 1/r is 3D-potential math used in
    /// a 2D domain (dimensionally wrong) and has a peaked plateau + algebraic tail —
    /// the wrong shape for "in-band / out-of-band" behavior. The Gaussian is
    /// flat-topped (saturating across the natural hover envelope) and
    /// super-exponentially compact (essentially no contribution beyond ~3σ), which
    /// is exactly the desired locality.</para>
    ///
    /// <para><b>Per-frame relative normalization.</b>
    /// <code>
    ///     V_norm = max_i φ_i  /  max_i q_i
    /// </code>
    /// i.e. "what fraction of the loudest available band's energy am I tapping
    /// right now". Sitting on the loudest peak ⇒ 1.0. Halfway between peaks ⇒
    /// whatever the Gaussian gives. Far from everything ⇒ ~0. The denominator
    /// breathes with the music, so the reading stays meaningful in both quiet and
    /// busy passages — a mosquito doesn't need a calorie counter, only to know
    /// whether it's on food relative to the best food currently available.</para>
    ///
    /// <para><b>Asymmetric capacitor.</b>
    /// <code>
    ///     dV/dt = (V_norm − V) / τ
    /// </code>
    /// with τ = <see cref="CapTauCharge"/> when filling and <see cref="CapTauDischarge"/>
    /// when draining. Snappy intake when sitting on a peak, slow bleed when
    /// leaving — "full" persists long enough for downstream components to drive
    /// intentional discharge-seeking behavior (e.g. Steering's Sated mood).</para>
    /// </summary>
    public sealed class Goal : Charge
    {
        // ── References ──
        private readonly Reactivity.Bar _bars;
        private readonly double _hoverOffset;

        // ── State ──
        /// <summary>Integrated charge in [0, 1]. Driven by the capacitor dynamics described on the class doc.</summary>
        private double _value;

        // ── Tunables ──
        /// <summary>
        /// Feeding radius (px) in the Gaussian kernel — the spatial scale at which
        /// a band's influence is felt. Tuned so that the goal's natural position
        /// oscillation (hover offset + wander radius, up to ~63px from a peak in
        /// worst alignment) stays inside the plateau and reads as fully fed. With
        /// σ=70, r=63 still gives ≈ 0.67 of saturation; far-field (r=200) drops to ~2%.
        /// </summary>
        private const double Sigma = 70.0;

        /// <summary>Capacitor time constant (seconds) while CHARGING — when the sampled potential exceeds current value. Snappy so the goal lights up when it locks onto a hot region.</summary>
        private const double CapTauCharge = 0.9;

        /// <summary>Capacitor time constant (seconds) while DISCHARGING — when the sampled potential is below current value. Deliberately slower than charging so "full" persists long enough to drive intentional behavior rather than evaporating the instant the goal drifts off a peak.</summary>
        private const double CapTauDischarge = 3.0;

        /// <summary>Charge level at or above which the goal is "armed" — Physics.Goal will fire collisions, Rendering.Goal reaches full glow envelope.</summary>
        private const double ArmThreshold = 0.40;

        // ── Public API ──
        /// <summary>Current integrated charge in [0, 1]. Read by other components via the lambda wiring set up in the owning entity.</summary>
        public double Value => _value;

        /// <summary>True once <see cref="Value"/> has reached <see cref="ArmThreshold"/>. Read by Physics.Goal to gate the trigger so a fresh-spawned goal doesn't instantly score on an overlapping ball.</summary>
        public bool IsArmed => _value >= ArmThreshold;

        /// <summary>
        /// Construct the sensor.
        /// </summary>
        /// <param name="bars">The Reactivity.Bar component producing the per-band heights this sensor samples.</param>
        /// <param name="hoverOffset">Pixels above each bar's top edge that the feeding-zone center is placed. Should match the steering component's hover target so the field's plateau aligns with where the goal naturally hovers.</param>
        public Goal(Reactivity.Bar bars, double hoverOffset)
        {
            _bars = bars;
            _hoverOffset = hoverOffset;
        }

        /// <inheritdoc />
        public override void Update(World entity, Size viewport, float dt)
        {
            if (!Enabled || dt <= 0) return;
            double w = viewport.Width, h = viewport.Height;
            if (w <= 0 || h <= 0) return;

            var bars = _bars.BarHeights;
            if (bars.Length == 0) return;

            double colWidth = w / bars.Length;
            double twoSigmaSq = 2.0 * Sigma * Sigma;
            double gx = entity.Position.X;
            double gy = entity.Position.Y;

            double maxPhi = 0;
            double maxQ = 0;
            for (int i = 0; i < bars.Length; i++)
            {
                double q = bars[i];
                if (q <= 0) continue;
                if (q > maxQ) maxQ = q;
                double bx = (i + 0.5) * colWidth;
                double by = h - q - _hoverOffset;
                double ddx = bx - gx;
                double ddy = by - gy;
                double phi = q * Math.Exp(-(ddx * ddx + ddy * ddy) / twoSigmaSq);
                if (phi > maxPhi) maxPhi = phi;
            }

            // If everything's silent there's nothing to feed from → 0; otherwise the
            // ratio is well-defined and bounded ≤ 1 (since φ_i ≤ q_i ≤ maxQ).
            double vNorm = maxQ > 0 ? Math.Clamp(maxPhi / maxQ, 0, 1) : 0;
            double tau = vNorm > _value ? CapTauCharge : CapTauDischarge;
            _value += (vNorm - _value) * (dt / tau);
            _value = Math.Clamp(_value, 0, 1);
        }
    }
    #endregion
}
