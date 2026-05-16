using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WavBall.Configuration;
using WavBall.Entities;
using Microsoft.Win32;

namespace WavBall.Components;

/// <summary>
/// Abstract base for all rendering behaviors. Subclasses override <see cref="Render"/>
/// to draw entity state onto a WPF DrawingContext each frame.
/// Concrete behaviors are nested types so the entire rendering surface lives in one file.
/// </summary>
public abstract class Rendering
{
    #region Pipeline
    /// <summary>
    /// Draw the entity to the given context. Default no-op.
    /// </summary>
    public virtual void Render(World entity, DrawingContext dc, Size viewport) { }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Bar
    /// <summary>
    /// Spectrum-bar renderer: clears the viewport with black (background layer)
    /// and draws each band as a vertical gradient bar using the Windows accent color.
    /// </summary>
    public sealed class Bar : Rendering
    {
        private readonly Reactivity.Bar _bars;
        private readonly Color _accentColor;
        private readonly Color _lighterColor;

        /// <summary>
        /// Create the renderer, reading the Windows accent color for the bar gradient.
        /// </summary>
        public Bar(Reactivity.Bar bars)
        {
            _bars = bars;

            _accentColor = GetWindowsAccentColor();
            _lighterColor = LightenColor(_accentColor, 0.5f);
        }

        /// <inheritdoc />
        public override void Render(World entity, DrawingContext dc, Size viewport)
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
    public sealed class Peak : Rendering
    {
        private readonly Reactivity.Bar _bars;
        private readonly Physics.Peak _peaks;
        private readonly Brush _peakBrush;

        /// <summary>
        /// Create the peak renderer, reading layout from bars and positions from peak physics.
        /// </summary>
        public Peak(Reactivity.Bar bars, Physics.Peak peaks)
        {
            _bars = bars;
            _peaks = peaks;

            _peakBrush = new SolidColorBrush(Colors.White);
            _peakBrush.Freeze();
        }

        /// <inheritdoc />
        public override void Render(World entity, DrawingContext dc, Size viewport)
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
    /// Sprite-based ball renderer. Loads a pre-generated PNG for each <see cref="BallKind"/>
    /// and draws it with rotation. A radial highlight overlay in world space provides
    /// a consistent 3D lighting effect across all ball types.
    ///
    /// AI-generated sprites have transparent padding around the visible ball and a
    /// drop-shadow on the bottom-right that don't correspond to the geometric ball.
    /// At construction we scan the alpha channel to find the visible ball's center and
    /// radius in sprite-pixel space, then at render time we draw the sprite scaled and
    /// translated so the visible ball circle exactly maps to the physics circle. This
    /// makes the rendered ball flush against bars/peaks at rest — no visible gap.
    /// </summary>
    public sealed class Ball : Rendering
    {
        private readonly Physics.Ball _physics;
        private readonly BitmapImage _sprite;
        private readonly RadialGradientBrush _highlightBrush;

        /// <summary>Visible ball center inside the sprite, normalized to [0,1] of sprite pixel dims.</summary>
        private readonly Point _spriteBallCenterNorm;

        /// <summary>
        /// Pre-multiplied scale factor applied to sprite pixel dims at render time so the
        /// visible ball diameter equals <c>physics.Radius * 2</c>. Equal to
        /// <c>physics.Radius / visibleBallRadiusInSpritePx</c>.
        /// </summary>
        private readonly double _spriteScale;

        public Ball(Physics.Ball physics)
        {
            _physics = physics;

            var uri = new Uri($"pack://application:,,,/Assets/Generated/{SpriteFileName(physics.Kind)}");
            _sprite = new BitmapImage(uri);
            RenderOptions.SetBitmapScalingMode(_sprite, BitmapScalingMode.Fant);
            _sprite.Freeze();

            (_spriteBallCenterNorm, _spriteScale) = MeasureVisibleBall(_sprite, physics.Radius);

            _highlightBrush = new RadialGradientBrush();
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0));
            _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(  0, 255, 255, 255), 0.7));
            _highlightBrush.Freeze();
        }

        /// <inheritdoc />
        public override void Render(World entity, DrawingContext dc, Size viewport)
        {
            var pos = entity.Position;
            double radius = _physics.Radius;

            // Map the sprite so its visible-ball center lands on `pos` and its visible-ball
            // radius equals `radius`. The sprite is drawn at its natural pixel dims times
            // _spriteScale; the rect origin is shifted so _spriteBallCenterNorm of the
            // upscaled sprite coincides with pos.
            double drawW = _sprite.PixelWidth  * _spriteScale;
            double drawH = _sprite.PixelHeight * _spriteScale;
            var rect = new Rect(
                pos.X - _spriteBallCenterNorm.X * drawW,
                pos.Y - _spriteBallCenterNorm.Y * drawH,
                drawW, drawH);

            // Clip to the physics circle so the sprite's transparent padding/shadow
            // (now sitting outside the visible ball after upscaling) gets trimmed cleanly.
            var clipGeometry = new EllipseGeometry(pos, radius, radius);
            clipGeometry.Freeze();
            dc.PushClip(clipGeometry);

            dc.PushTransform(new RotateTransform(_physics.Rotation, pos.X, pos.Y));
            dc.DrawImage(_sprite, rect);
            dc.Pop(); // rotation

            dc.Pop(); // clip

            // Highlight in world space (light source doesn't rotate with the ball)
            var highlightCenter = new Point(pos.X - radius * 0.25, pos.Y - radius * 0.25);
            dc.DrawEllipse(_highlightBrush, null, highlightCenter, radius * 0.6, radius * 0.6);
        }

        /// <summary>
        /// Scan the sprite's alpha channel to locate the visible ball's center and radius
        /// in sprite-pixel space, then return (center normalized to sprite dims, scale to
        /// apply at render time so the visible diameter equals <c>2 * targetRadius</c>).
        ///
        /// Strategy: find the topmost opaque row (shadow-free since shadows fall below the
        /// ball), then scan rows in the upper half to find the widest opaque span — that's
        /// the ball's equator. Width gives diameter; assuming circularity, the vertical
        /// center is <c>topRow + radius</c>. Limiting the search to the upper half keeps
        /// the bottom-right drop-shadow from inflating the measurement.
        /// </summary>
        private static (Point centerNorm, double scale) MeasureVisibleBall(BitmapImage source, double targetRadius)
        {
            // Ensure BGRA32 so byte index 3 of every 4-byte pixel is alpha
            BitmapSource fmt = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int w = fmt.PixelWidth, h = fmt.PixelHeight;
            int stride = w * 4;
            byte[] px = new byte[stride * h];
            fmt.CopyPixels(px, stride, 0);

            // High threshold excludes anti-aliased edges and faint shadow halos
            const byte AlphaThreshold = 200;

            int topRow = -1;
            for (int y = 0; y < h && topRow < 0; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] >= AlphaThreshold) { topRow = y; break; }

            if (topRow < 0) return (new Point(0.5, 0.5), 1.0); // empty sprite — no-op

            // Search the upper half only — shadow is always below the ball
            int searchEnd = Math.Min(h, topRow + h / 2);
            int bestLeft = 0, bestRight = 0, bestWidth = 0;
            for (int y = topRow; y < searchEnd; y++)
            {
                int left = -1, right = -1;
                for (int x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] >= AlphaThreshold) { left = x; break; }
                for (int x = w - 1; x >= 0; x--)
                    if (px[y * stride + x * 4 + 3] >= AlphaThreshold) { right = x; break; }

                if (left < 0 || right <= left) continue;
                int width = right - left;
                if (width > bestWidth) { bestWidth = width; bestLeft = left; bestRight = right; }
            }

            if (bestWidth == 0) return (new Point(0.5, 0.5), 1.0);

            double ballRadiusPx = bestWidth / 2.0;
            double ballCenterX = (bestLeft + bestRight) / 2.0;
            double ballCenterY = topRow + ballRadiusPx; // circular assumption — ball center sits one radius below the top

            return (
                new Point(ballCenterX / w, ballCenterY / h),
                targetRadius / ballRadiusPx);
        }

        private static string SpriteFileName(BallKind kind) => kind switch
        {
            BallKind.BeachBall   => "beach-ball.png",
            BallKind.Racquetball => "racquetball.png",
            BallKind.TennisBall  => "tennis-ball.png",
            BallKind.SoccerBall  => "soccer-ball.png",
            BallKind.Basketball  => "basketball.png",
            BallKind.Baseball    => "baseball.png",
            BallKind.BowlingBall => "bowling-ball.png",
            _                    => "beach-ball.png",
        };
    }
    #endregion

    // ─────────────────────────────────────────────────────────────────────────
    #region Nested: Goal
    /// <summary>
    /// Goal-zone renderer: draws a pulsing golden ring that the player must guide the ball into.
    /// The pulse is time-driven (not audio-reactive) for consistent visibility.
    /// </summary>
    public sealed class Goal : Rendering
    {
        private readonly double _radius;
        private double _pulsePhase;

        /// <summary>
        /// When false, the goal is suppressed and not rendered at all.
        /// Mirrors <see cref="Physics.Goal.Enabled"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Reference to the ball entity. When set and alive, the goal ring shifts
        /// slightly toward the ball each frame — a visual "gravitational lean" that
        /// animates the attraction and makes the ring feel alive.
        /// </summary>
        public World? BallRef { get; set; }

        /// <summary>Maximum visual offset (px) the goal can shift toward the ball.</summary>
        private const double MaxVisualOffset = 12.0;

        /// <summary>Distance (px) at which the offset reaches maximum. Closer = capped.</summary>
        private const double OffsetSoftening = 80.0;

        /// <summary>Per-frame exponential lerp factor for smooth offset transitions.</summary>
        private const double OffsetLerpRate = 0.08;

        /// <summary>Smoothed visual offset applied to the rendered position.</summary>
        private Vector _visualOffset;

        public Goal(double radius) { _radius = radius; }

        /// <inheritdoc />
        public override void Render(World entity, DrawingContext dc, Size viewport)
        {
            if (!Enabled) return;

            // Compute visual offset toward ball (gravitational lean).
            // The goal's actual Position stays fixed for collision; only the
            // rendered position shifts, giving the ring a subtle living motion.
            Vector targetOffset = default;
            if (BallRef is { IsAlive: true })
            {
                var diff = BallRef.Position - entity.Position;
                double dist = diff.Length;
                if (dist > 1.0)
                {
                    double magnitude = Math.Min(MaxVisualOffset,
                                                MaxVisualOffset * OffsetSoftening / dist);
                    targetOffset = (diff / dist) * magnitude;
                }
            }
            _visualOffset += (targetOffset - _visualOffset) * OffsetLerpRate;
            var pos = entity.Position + _visualOffset;

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
    public sealed class Particle : Rendering
    {
        private readonly ParticlePool _pool;

        /// <summary>
        /// Create a particle renderer operating on the given pool's buffer.
        /// </summary>
        public Particle(ParticlePool pool) { _pool = pool; }

        /// <inheritdoc />
        public override void Render(World entity, DrawingContext dc, Size viewport)
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
