using System;
using System.Windows;
using System.Windows.Media;
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
        private readonly Brush _barBrush;

        /// <summary>
        /// Create the renderer, reading the Windows accent color for the bar gradient.
        /// </summary>
        public Bar(ReactivityComponent.Bar bars)
        {
            _bars = bars;

            var accent = GetWindowsAccentColor();
            var lighter = LightenColor(accent, 0.5f);

            _barBrush = new LinearGradientBrush(accent, lighter, new Point(0, 1), new Point(0, 0));
            _barBrush.Freeze();
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

            for (int i = 0; i < barHeights.Length; i++)
            {
                double x = i * barWidth;
                double height = Math.Clamp(barHeights[i], 0, viewport.Height);
                double y = viewport.Height - height;

                if (height > 1)
                    dc.DrawRectangle(_barBrush, null, new Rect(x, y, drawWidth, height));
            }
        }

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
    /// Beach-ball renderer: 4 colored wedges (red, yellow, blue, white) plus a
    /// radial highlight for a 3D effect. Reads radius from the sibling physics component.
    /// </summary>
    public sealed class Ball : RenderingComponent
    {
        private readonly PhysicsComponent.Ball _physics;
        private readonly Brush[] _stripeBrushes;
        private readonly Pen _outlinePen;
        private readonly RadialGradientBrush _highlightBrush;

        /// <summary>
        /// Create the renderer with classic beach ball colors.
        /// </summary>
        public Ball(PhysicsComponent.Ball physics)
        {
            _physics = physics;

            _stripeBrushes = new Brush[]
            {
                new SolidColorBrush(Color.FromRgb(220, 50, 50)),   // Red
                new SolidColorBrush(Color.FromRgb(255, 220, 50)),  // Yellow
                new SolidColorBrush(Color.FromRgb(50, 120, 220)),  // Blue
                new SolidColorBrush(Colors.White),                  // White
            };
            foreach (var brush in _stripeBrushes) brush.Freeze();

            _outlinePen = new Pen(Brushes.White, 2);
            _outlinePen.Freeze();

            _highlightBrush = new RadialGradientBrush();
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0));
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(  0, 255, 255, 255), 0.7));
            _highlightBrush.Freeze();
        }

        /// <inheritdoc />
        public override void Render(SceneEntity entity, DrawingContext dc, Size viewport)
        {
            var pos = entity.Position;
            double radius = _physics.Radius;

            // Apply rotation transform around the ball's center so stripes spin with the body.
            // Highlight stays world-aligned (it represents a fixed light source), so we pop after wedges.
            dc.PushTransform(new RotateTransform(_physics.Rotation, pos.X, pos.Y));

            int stripeCount = _stripeBrushes.Length;
            double anglePerStripe = 360.0 / stripeCount;
            for (int i = 0; i < stripeCount; i++)
            {
                double startAngle = i * anglePerStripe - 90;
                var segment = CreateWedgeGeometry(pos, radius, startAngle, anglePerStripe);
                dc.DrawGeometry(_stripeBrushes[i], null, segment);
            }

            dc.DrawEllipse(null, _outlinePen, pos, radius, radius);

            dc.Pop();

            // Highlight is rendered in world space (light source doesn't rotate with the ball)
            var highlightCenter = new Point(pos.X - radius * 0.25, pos.Y - radius * 0.25);
            dc.DrawEllipse(_highlightBrush, null, highlightCenter, radius * 0.6, radius * 0.6);
        }

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
                    byte sizeAlpha = (byte)Math.Clamp(p.Color.A * (0.20 + 0.55 * (p.Size / 1.8)), 0, 255);
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
