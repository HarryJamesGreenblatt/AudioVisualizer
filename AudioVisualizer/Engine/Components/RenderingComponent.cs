using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Configuration;
using AudioVisualizer.Engine.Entities;
using Microsoft.Win32;

namespace AudioVisualizer.Engine.Components;

/// <summary>
/// Abstract base for all rendering behaviors. Subclasses override <see cref="Render"/>
/// to draw entity state onto a WPF DrawingContext each frame.
/// Concrete behaviors are nested types so the entire rendering surface lives in one file.
/// </summary>
public abstract class RenderingComponent
{
    #region Pipeline
    /// <summary>
    /// Draw the entity to the given context. Default no-op.
    /// </summary>
    public virtual void Render(SceneEntity entity, DrawingContext dc, Size viewport) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Bar
    /// <summary>
    /// Spectrum-bar renderer: clears the viewport with black (background layer)
    /// and draws each band as a vertical gradient bar using the Windows accent color.
    /// </summary>
    public sealed class Bar : RenderingComponent
    {
        private readonly ReactivityComponent.Bar _bars;
        private readonly Color _accentColor;
        private readonly Color _lighterColor;

        /// <summary>
        /// Create the renderer, reading the Windows accent color for the bar gradient.
        /// </summary>
        public Bar(ReactivityComponent.Bar bars)
        {
            _bars = bars;

            _accentColor = GetWindowsAccentColor();
            _lighterColor = LightenColor(_accentColor, 0.5f);
        }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
        {
            // Clear background — bars are the bottom render layer
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, viewport.Width, viewport.Height));

            var barHeights = _bars.BarHeights;
            if (barHeights.Length == 0) return;

            double barWidth = viewport.Width / barHeights.Length;
            double gap = Math.Max(1, barWidth * 0.15);
            double drawWidth = barWidth - gap;

            // Per-band thermal luminosity: each bar glows according to its own heat —
            // how much sustained activity its frequency region has seen recently.
            // Cold bars are dim, hot bars blaze. No instantaneous flicker.
            var bandHeat = _bars.BandHeat;

            for (int i = 0; i < barHeights.Length; i++)
            {
                double height = Math.Clamp(barHeights[i], 0, viewport.Height);
                if (height <= 1) continue;

                double x = i * barWidth;
                double y = viewport.Height - height;

                // Heat is the primary luminosity driver (0–0.75), height adds a small
                // baseline (0.10–0.25) so even cold tall bars aren't invisible.
                float heat = i < bandHeat.Length ? bandHeat[i] : 0f;
                float normalizedH = (float)(height / viewport.Height);
                float luminosity = Math.Clamp(0.10f + 0.15f * normalizedH + heat * 0.75f, 0f, 1f);

                // Modulate the accent gradient colors by luminosity
                var bottom = ScaleColor(_accentColor, luminosity);
                var top = ScaleColor(_lighterColor, luminosity);
                var brush = new LinearGradientBrush(bottom, top, new Point(0, 1), new Point(0, 0));
                brush.Freeze();

                dc.DrawRectangle(brush, null, new Rect(x, y, drawWidth, height));
            }
        }

        /// <summary>
        /// Scale a color's brightness by a factor (0 = black, 1 = original).
        /// </summary>
        private static Color ScaleColor(Color c, float factor) => Color.FromArgb(
            c.A,
            (byte)(c.R * factor),
            (byte)(c.G * factor),
            (byte)(c.B * factor));

        /// <summary>
        /// Read the Windows accent color from DWM, with blue fallback.
        /// </summary>
        private static Color GetWindowsAccentColor()
        {
            try
            {
                var value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                    "AccentColor", null);

                if (value is int abgr)
                {
                    byte b = (byte)((abgr >> 16) & 0xFF);
                    byte g = (byte)((abgr >> 8)  & 0xFF);
                    byte r = (byte)(abgr & 0xFF);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch { /* fall through */ }
            return Color.FromRgb(0, 120, 215);
        }

        /// <summary>
        /// Lighten a color by the given factor (0 = unchanged, 1 = white).
        /// </summary>
        private static Color LightenColor(Color c, float factor) => Color.FromRgb(
            (byte)(c.R + (255 - c.R) * factor),
            (byte)(c.G + (255 - c.G) * factor),
            (byte)(c.B + (255 - c.B) * factor));
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Peak
    /// <summary>
    /// Peak-marker renderer: draws white horizontal lines at the current peak height for each band.
    /// </summary>
    public sealed class Peak : RenderingComponent
    {
        private readonly ReactivityComponent.Bar _bars;
        private readonly PhysicsComponent.Peak _peaks;
        private readonly Brush _peakBrush;

        /// <summary>
        /// Create the peak renderer, reading layout from bars and positions from peak physics.
        /// </summary>
        public Peak(ReactivityComponent.Bar bars, PhysicsComponent.Peak peaks)
        {
            _bars = bars;
            _peaks = peaks;

            _peakBrush = new SolidColorBrush(Colors.White);
            _peakBrush.Freeze();
        }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
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
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Ball
    /// <summary>
    /// Ball renderer that dispatches to per-type drawing methods based on <see cref="BallKind"/>.
    /// Each ball type has its own visual identity: beach ball stripes, basketball seams,
    /// tennis ball felt, etc. All share a common radial highlight for 3D effect.
    /// </summary>
    public sealed class Ball : RenderingComponent
    {
        private readonly PhysicsComponent.Ball _physics;
        private readonly RadialGradientBrush _highlightBrush;

        // Beach ball
        private readonly Brush[]? _stripeBrushes;

        // Basketball
        private readonly Brush? _basketballFill;
        private readonly Pen? _basketballSeamPen;

        // Tennis ball
        private readonly Brush? _tennisFill;
        private readonly Pen? _tennisSeamPen;

        // Soccer ball
        private readonly Brush? _soccerFill;
        private readonly Pen? _soccerPanelPen;

        // Baseball
        private readonly Brush? _baseballFill;
        private readonly Pen? _baseballStitchPen;

        // Racquetball
        private readonly Brush? _racquetballFill;

        // Bowling ball
        private readonly Brush? _bowlingFill;
        private readonly Brush? _bowlingHoleBrush;

        // Shared
        private readonly Pen _outlinePen;

        public Ball(PhysicsComponent.Ball physics)
        {
            _physics = physics;

            _highlightBrush = new RadialGradientBrush();
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0));
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(  0, 255, 255, 255), 0.7));
            _highlightBrush.Freeze();

            switch (physics.Kind)
            {
                case BallKind.BeachBall:
                    _stripeBrushes = new Brush[]
                    {
                        new SolidColorBrush(Color.FromRgb(220, 50, 50)),
                        new SolidColorBrush(Color.FromRgb(255, 220, 50)),
                        new SolidColorBrush(Color.FromRgb(50, 120, 220)),
                        new SolidColorBrush(Colors.White),
                    };
                    foreach (var b in _stripeBrushes) b.Freeze();
                    _outlinePen = new Pen(Brushes.White, 2); break;

                case BallKind.Basketball:
                    _basketballFill = new SolidColorBrush(Color.FromRgb(200, 100, 20)); _basketballFill.Freeze();
                    _basketballSeamPen = new Pen(new SolidColorBrush(Color.FromRgb(40, 20, 5)), 1.5); _basketballSeamPen.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(60, 30, 10)), 2); break;

                case BallKind.TennisBall:
                    _tennisFill = new SolidColorBrush(Color.FromRgb(200, 220, 50)); _tennisFill.Freeze();
                    _tennisSeamPen = new Pen(Brushes.White, 1.5); _tennisSeamPen.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(160, 180, 40)), 1.5); break;

                case BallKind.SoccerBall:
                    _soccerFill = Brushes.White;
                    _soccerPanelPen = new Pen(new SolidColorBrush(Color.FromRgb(30, 30, 30)), 1.2); _soccerPanelPen.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(80, 80, 80)), 1.5); break;

                case BallKind.Baseball:
                    _baseballFill = new SolidColorBrush(Color.FromRgb(245, 240, 230)); _baseballFill.Freeze();
                    _baseballStitchPen = new Pen(new SolidColorBrush(Color.FromRgb(200, 40, 40)), 1.2); _baseballStitchPen.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(180, 175, 165)), 1.5); break;

                case BallKind.Racquetball:
                    _racquetballFill = new SolidColorBrush(Color.FromRgb(30, 100, 220)); _racquetballFill.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(20, 70, 160)), 1.5); break;

                case BallKind.BowlingBall:
                    _bowlingFill = new SolidColorBrush(Color.FromRgb(25, 25, 35)); _bowlingFill.Freeze();
                    _bowlingHoleBrush = new SolidColorBrush(Color.FromRgb(15, 15, 20)); _bowlingHoleBrush.Freeze();
                    _outlinePen = new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 60)), 2); break;

                default:
                    _outlinePen = new Pen(Brushes.White, 2); break;
            }
            _outlinePen.Freeze();
        }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
        {
            var pos = entity.Position;
            double radius = _physics.Radius;

            dc.PushTransform(new RotateTransform(_physics.Rotation, pos.X, pos.Y));

            switch (_physics.Kind)
            {
                case BallKind.BeachBall:    RenderBeachBall(dc, pos, radius); break;
                case BallKind.Basketball:   RenderBasketball(dc, pos, radius); break;
                case BallKind.TennisBall:   RenderTennisBall(dc, pos, radius); break;
                case BallKind.SoccerBall:   RenderSoccerBall(dc, pos, radius); break;
                case BallKind.Baseball:     RenderBaseball(dc, pos, radius); break;
                case BallKind.Racquetball:  RenderRacquetball(dc, pos, radius); break;
                case BallKind.BowlingBall:  RenderBowlingBall(dc, pos, radius); break;
            }

            dc.Pop();

            // Highlight is rendered in world space (light source doesn't rotate with the ball)
            var highlightCenter = new Point(pos.X - radius * 0.25, pos.Y - radius * 0.25);
            dc.DrawEllipse(_highlightBrush, null, highlightCenter, radius * 0.6, radius * 0.6);
        }

        #region Per-type renderers

        private void RenderBeachBall(DrawingContext dc, Point pos, double radius)
        {
            int stripeCount = _stripeBrushes!.Length;
            double anglePerStripe = 360.0 / stripeCount;
            for (int i = 0; i < stripeCount; i++)
            {
                double startAngle = i * anglePerStripe - 90;
                var segment = CreateWedgeGeometry(pos, radius, startAngle, anglePerStripe);
                dc.DrawGeometry(_stripeBrushes[i], null, segment);
            }
            dc.DrawEllipse(null, _outlinePen, pos, radius, radius);
        }

        private void RenderBasketball(DrawingContext dc, Point pos, double radius)
        {
            dc.DrawEllipse(_basketballFill, _outlinePen, pos, radius, radius);

            // Horizontal seam
            dc.DrawLine(_basketballSeamPen!, new Point(pos.X - radius * 0.9, pos.Y),
                                             new Point(pos.X + radius * 0.9, pos.Y));

            // Vertical seam (slight curve via 3-point polyline)
            dc.DrawLine(_basketballSeamPen!, new Point(pos.X, pos.Y - radius * 0.9),
                                             new Point(pos.X, pos.Y + radius * 0.9));

            // Two curved cross-seams
            double cx = radius * 0.55;
            var leftArc = new PathGeometry();
            var lf = new PathFigure { StartPoint = new Point(pos.X - cx, pos.Y - radius * 0.7), IsFilled = false };
            lf.Segments.Add(new BezierSegment(
                new Point(pos.X - cx * 1.6, pos.Y - radius * 0.15),
                new Point(pos.X - cx * 1.6, pos.Y + radius * 0.15),
                new Point(pos.X - cx, pos.Y + radius * 0.7), true));
            leftArc.Figures.Add(lf); leftArc.Freeze();
            dc.DrawGeometry(null, _basketballSeamPen, leftArc);

            var rightArc = new PathGeometry();
            var rf = new PathFigure { StartPoint = new Point(pos.X + cx, pos.Y - radius * 0.7), IsFilled = false };
            rf.Segments.Add(new BezierSegment(
                new Point(pos.X + cx * 1.6, pos.Y - radius * 0.15),
                new Point(pos.X + cx * 1.6, pos.Y + radius * 0.15),
                new Point(pos.X + cx, pos.Y + radius * 0.7), true));
            rightArc.Figures.Add(rf); rightArc.Freeze();
            dc.DrawGeometry(null, _basketballSeamPen, rightArc);
        }

        private void RenderTennisBall(DrawingContext dc, Point pos, double radius)
        {
            dc.DrawEllipse(_tennisFill, _outlinePen, pos, radius, radius);

            // Characteristic curved seam (two mirrored S-curves)
            double r = radius * 0.85;
            var seam1 = new PathGeometry();
            var f1 = new PathFigure { StartPoint = new Point(pos.X - r * 0.3, pos.Y - r), IsFilled = false };
            f1.Segments.Add(new BezierSegment(
                new Point(pos.X + r * 0.6, pos.Y - r * 0.3),
                new Point(pos.X - r * 0.6, pos.Y + r * 0.3),
                new Point(pos.X + r * 0.3, pos.Y + r), true));
            seam1.Figures.Add(f1); seam1.Freeze();
            dc.DrawGeometry(null, _tennisSeamPen, seam1);

            var seam2 = new PathGeometry();
            var f2 = new PathFigure { StartPoint = new Point(pos.X + r * 0.3, pos.Y - r), IsFilled = false };
            f2.Segments.Add(new BezierSegment(
                new Point(pos.X - r * 0.6, pos.Y - r * 0.3),
                new Point(pos.X + r * 0.6, pos.Y + r * 0.3),
                new Point(pos.X - r * 0.3, pos.Y + r), true));
            seam2.Figures.Add(f2); seam2.Freeze();
            dc.DrawGeometry(null, _tennisSeamPen, seam2);
        }

        private void RenderSoccerBall(DrawingContext dc, Point pos, double radius)
        {
            dc.DrawEllipse(_soccerFill, _outlinePen, pos, radius, radius);

            // Central pentagon
            DrawPentagon(dc, pos, radius * 0.35, _soccerPanelPen!, filled: true);

            // Surrounding pentagons (5, evenly spaced around the edge)
            for (int i = 0; i < 5; i++)
            {
                double angle = i * 72.0 - 90;
                double rad = angle * Math.PI / 180.0;
                var center = new Point(pos.X + radius * 0.62 * Math.Cos(rad),
                                       pos.Y + radius * 0.62 * Math.Sin(rad));
                DrawPentagon(dc, center, radius * 0.22, _soccerPanelPen!, filled: true);
            }
        }

        private static void DrawPentagon(DrawingContext dc, Point center, double size, Pen pen, bool filled)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure();
            for (int i = 0; i < 5; i++)
            {
                double angle = (i * 72 - 90) * Math.PI / 180.0;
                var pt = new Point(center.X + size * Math.Cos(angle), center.Y + size * Math.Sin(angle));
                if (i == 0) figure.StartPoint = pt;
                else figure.Segments.Add(new LineSegment(pt, true));
            }
            figure.IsClosed = true;
            geometry.Figures.Add(figure);
            geometry.Freeze();
            dc.DrawGeometry(filled ? new SolidColorBrush(Color.FromRgb(30, 30, 30)) : null, pen, geometry);
        }

        private void RenderBaseball(DrawingContext dc, Point pos, double radius)
        {
            dc.DrawEllipse(_baseballFill, _outlinePen, pos, radius, radius);

            // Red stitching — two mirrored C-curves
            double r = radius * 0.75;
            for (int side = -1; side <= 1; side += 2)
            {
                double ox = side * radius * 0.45;
                var stitch = new PathGeometry();
                var fig = new PathFigure
                {
                    StartPoint = new Point(pos.X + ox, pos.Y - r),
                    IsFilled = false
                };
                fig.Segments.Add(new BezierSegment(
                    new Point(pos.X + ox + side * r * 0.8, pos.Y - r * 0.35),
                    new Point(pos.X + ox + side * r * 0.8, pos.Y + r * 0.35),
                    new Point(pos.X + ox, pos.Y + r), true));
                stitch.Figures.Add(fig);
                stitch.Freeze();
                dc.DrawGeometry(null, _baseballStitchPen, stitch);

                // Stitch tick marks along the curve
                int ticks = 6;
                for (int t = 1; t < ticks; t++)
                {
                    double frac = t / (double)ticks;
                    double ty = pos.Y - r + frac * 2 * r;
                    double bulge = Math.Sin(frac * Math.PI) * side * r * 0.8;
                    double tx = pos.X + ox + bulge * 0.7;
                    double tickLen = radius * 0.08;
                    dc.DrawLine(_baseballStitchPen!, new Point(tx - tickLen, ty - tickLen),
                                                     new Point(tx + tickLen, ty + tickLen));
                }
            }
        }

        private void RenderRacquetball(DrawingContext dc, Point pos, double radius)
        {
            dc.DrawEllipse(_racquetballFill, _outlinePen, pos, radius, radius);
        }

        private void RenderBowlingBall(DrawingContext dc, Point pos, double radius)
        {
            // Dark glossy body with subtle radial gradient
            var glossBrush = new RadialGradientBrush();
            glossBrush.GradientOrigin = new Point(0.35, 0.35);
            glossBrush.GradientStops.Add(new GradientStop(Color.FromRgb(60, 60, 80), 0.0));
            glossBrush.GradientStops.Add(new GradientStop(Color.FromRgb(20, 20, 30), 1.0));
            glossBrush.Freeze();
            dc.DrawEllipse(glossBrush, _outlinePen, pos, radius, radius);

            // Three finger holes in a triangle near the top
            double holeR = radius * 0.1;
            double holeY = pos.Y - radius * 0.3;
            dc.DrawEllipse(_bowlingHoleBrush, null, new Point(pos.X - radius * 0.2, holeY), holeR, holeR);
            dc.DrawEllipse(_bowlingHoleBrush, null, new Point(pos.X + radius * 0.2, holeY), holeR, holeR);
            dc.DrawEllipse(_bowlingHoleBrush, null, new Point(pos.X, holeY - radius * 0.22), holeR, holeR);
        }

        #endregion

        /// <summary>
        /// Build a pie-slice geometry for one beach-ball stripe.
        /// </summary>
        private static PathGeometry CreateWedgeGeometry(Point center, double radius, double startAngleDeg, double angleDeg)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = center };

            double startAngle = startAngleDeg * Math.PI / 180.0;
            double endAngle   = (startAngleDeg + angleDeg) * Math.PI / 180.0;

            var startPoint = new Point(
                center.X + radius * Math.Cos(startAngle),
                center.Y + radius * Math.Sin(startAngle));
            var endPoint = new Point(
                center.X + radius * Math.Cos(endAngle),
                center.Y + radius * Math.Sin(endAngle));

            figure.Segments.Add(new LineSegment(startPoint, true));
            figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0,
                angleDeg > 180, SweepDirection.Clockwise, true));
            figure.Segments.Add(new LineSegment(center, true));

            figure.IsClosed = true;
            geometry.Figures.Add(figure);
            geometry.Freeze();
            return geometry;
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Goal
    /// <summary>
    /// Goal-zone renderer: draws a pulsing golden ring that the player must guide the ball into.
    /// The pulse is time-driven (not audio-reactive) for consistent visibility.
    /// </summary>
    public sealed class Goal : RenderingComponent
    {
        private readonly double _radius;
        private double _pulsePhase;

        /// <summary>
        /// When false, the goal is suppressed and not rendered at all.
        /// Mirrors <see cref="PhysicsComponent.Goal.Enabled"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        public Goal(double radius) { _radius = radius; }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
        {
            if (!Enabled) return;

            var pos = entity.Position;

            // Slow sine pulse for glow intensity
            _pulsePhase += 0.03;
            double pulse = 0.5 + 0.5 * Math.Sin(_pulsePhase);
            byte glowAlpha = (byte)(40 + 50 * pulse);

            // Outer glow
            var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(glowAlpha, 255, 215, 0)), 14 + 4 * pulse);
            glowPen.Freeze();
            dc.DrawEllipse(null, glowPen, pos, _radius, _radius);

            // Inner ring
            byte ringAlpha = (byte)(180 + 60 * pulse);
            var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(ringAlpha, 255, 215, 0)), 3);
            ringPen.Freeze();
            dc.DrawEllipse(null, ringPen, pos, _radius, _radius);

            // Small crosshair at center
            byte crossAlpha = (byte)(100 + 40 * pulse);
            var crossPen = new Pen(new SolidColorBrush(Color.FromArgb(crossAlpha, 255, 215, 0)), 1);
            crossPen.Freeze();
            double c = 6;
            dc.DrawLine(crossPen, new Point(pos.X - c, pos.Y), new Point(pos.X + c, pos.Y));
            dc.DrawLine(crossPen, new Point(pos.X, pos.Y - c), new Point(pos.X, pos.Y + c));
        }
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Particle
    /// <summary>
    /// Particle-pool renderer: iterates the pool's packed struct buffer and draws each
    /// live particle as an alpha-fading dot.
    /// </summary>
    public sealed class Particle : RenderingComponent
    {
        private readonly ParticlePool _pool;

        /// <summary>
        /// Create a particle renderer operating on the given pool's buffer.
        /// </summary>
        public Particle(ParticlePool pool) { _pool = pool; }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
        {
            var buffer = _pool.Buffer;
            for (int i = 0; i < buffer.Length; i++)
            {
                ref var p = ref buffer[i];
                if (p.FramesLeft <= 0) continue;

                if (p.Kind == ParticlePool.ParticleKind.RainDrop)
                {
                    // Real motion-blur trail: a polyline through the drop's actual past
                    // positions [Trail3 → Trail2 → Trail1 → Trail0 → Position]. Each segment
                    // has its OWN pen with falling alpha (and slightly tapered width) so the
                    // trail reads as a streak that fades from bright head to faint tail —
                    // the way photographic motion blur actually looks — rather than a
                    // uniform-opacity worm.
                    double sizeSqrt = Math.Sqrt(Math.Max(p.Size, 0.1));
                    double headWidth = 0.6 * sizeSqrt + 0.3;          // ~0.5–1.1 px

                    // Per-drop overall brightness from size (small = atmospheric, large = featured)
                    byte sizeAlpha = (byte)Math.Clamp(p.Color.A * (0.35 + 0.65 * (p.Size / 1.8)), 0, 255);
                    byte headAlpha = (byte)Math.Min(sizeAlpha, p.FramesLeft * 8);

                    // Newest segment (head) → oldest (tail). Alpha falls off geometrically;
                    // width tapers gently so the head feels the most "present".
                    // Helper: draw one segment with a per-segment alpha and width multiplier.
                    // Inlined four times because local functions can't capture `ref` locals.
                    if (p.TrailLen >= 1)
                    {
                        byte segA = (byte)Math.Clamp(headAlpha * 1.00, 0, 255);
                        if (segA >= 4)
                        {
                            var brush = new SolidColorBrush(Color.FromArgb(segA, p.Color.R, p.Color.G, p.Color.B));
                            brush.Freeze();
                            var pen = new Pen(brush, headWidth * 1.00); pen.Freeze();
                            dc.DrawLine(pen, p.Trail0, p.Position);
                        }
                    }
                    if (p.TrailLen >= 2)
                    {
                        byte segA = (byte)Math.Clamp(headAlpha * 0.65, 0, 255);
                        if (segA >= 4)
                        {
                            var brush = new SolidColorBrush(Color.FromArgb(segA, p.Color.R, p.Color.G, p.Color.B));
                            brush.Freeze();
                            var pen = new Pen(brush, headWidth * 0.90); pen.Freeze();
                            dc.DrawLine(pen, p.Trail1, p.Trail0);
                        }
                    }
                    if (p.TrailLen >= 3)
                    {
                        byte segA = (byte)Math.Clamp(headAlpha * 0.35, 0, 255);
                        if (segA >= 4)
                        {
                            var brush = new SolidColorBrush(Color.FromArgb(segA, p.Color.R, p.Color.G, p.Color.B));
                            brush.Freeze();
                            var pen = new Pen(brush, headWidth * 0.78); pen.Freeze();
                            dc.DrawLine(pen, p.Trail2, p.Trail1);
                        }
                    }
                    if (p.TrailLen >= 4)
                    {
                        byte segA = (byte)Math.Clamp(headAlpha * 0.15, 0, 255);
                        if (segA >= 4)
                        {
                            var brush = new SolidColorBrush(Color.FromArgb(segA, p.Color.R, p.Color.G, p.Color.B));
                            brush.Freeze();
                            var pen = new Pen(brush, headWidth * 0.65); pen.Freeze();
                            dc.DrawLine(pen, p.Trail3, p.Trail2);
                        }
                    }

                    // Drops without enough history yet (just spawned) render as a tiny dot
                    // at full head alpha so the spawn moment isn't invisible.
                    if (p.TrailLen == 0)
                    {
                        var brush = new SolidColorBrush(Color.FromArgb(headAlpha, p.Color.R, p.Color.G, p.Color.B));
                        brush.Freeze();
                        dc.DrawEllipse(brush, null, p.Position, headWidth, headWidth);
                    }
                }
                else
                {
                    byte alpha = (byte)Math.Clamp(p.FramesLeft * 8, 0, 255);
                    var color = Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    dc.DrawEllipse(brush, null, p.Position, 2, 2);
                }
            }
        }
    }
    #endregion
}
