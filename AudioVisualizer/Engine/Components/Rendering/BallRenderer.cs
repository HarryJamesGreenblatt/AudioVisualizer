using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Components.Physics;

namespace AudioVisualizer.Engine.Components.Rendering;

/// <summary>
/// Render component for beach ball entities.
/// Draws a colorful 4-stripe ball with a radial highlight for a 3D effect.
/// Reads radius from a sibling BallPhysics component on the same entity.
/// </summary>
public sealed class BallRenderer : IRenderingComponent
{
    #region Fields
    /// <summary>
    /// Reference to the physics component to get the ball radius.
    /// </summary>
    private readonly BallPhysics _physics;

    /// <summary>
    /// Stripe colors for the beach ball (classic red, yellow, blue, white pattern).
    /// </summary>
    private readonly Brush[] _stripeBrushes;

    /// <summary>
    /// Outline pen for the ball perimeter.
    /// </summary>
    private readonly Pen _outlinePen;

    /// <summary>
    /// Highlight brush for 3D shading effect.
    /// </summary>
    private readonly RadialGradientBrush _highlightBrush;
    #endregion

    #region Constructor
    /// <summary>
    /// Create the renderer with classic beach ball colors.
    /// </summary>
    /// <param name="physics">Physics component providing ball radius.</param>
    public BallRenderer(BallPhysics physics)
    {
        _physics = physics;

        _stripeBrushes = new Brush[]
        {
            new SolidColorBrush(Color.FromRgb(220, 50, 50)),   // Red
            new SolidColorBrush(Color.FromRgb(255, 220, 50)),  // Yellow
            new SolidColorBrush(Color.FromRgb(50, 120, 220)),  // Blue
            new SolidColorBrush(Colors.White),                  // White
        };
        foreach (var brush in _stripeBrushes)
            brush.Freeze();

        _outlinePen = new Pen(Brushes.White, 2);
        _outlinePen.Freeze();

        _highlightBrush = new RadialGradientBrush();
        _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0));
        _highlightBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7));
        _highlightBrush.Freeze();
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void Render(SceneEntity entity, DrawingContext dc, Size viewport)
    {
        var pos = entity.Position;
        double radius = _physics.Radius;

        // Draw 4 colored wedges
        int stripeCount = _stripeBrushes.Length;
        double anglePerStripe = 360.0 / stripeCount;

        for (int i = 0; i < stripeCount; i++)
        {
            double startAngle = i * anglePerStripe - 90;
            var segment = CreateWedgeGeometry(pos, radius, startAngle, anglePerStripe);
            dc.DrawGeometry(_stripeBrushes[i], null, segment);
        }

        // Outline circle
        dc.DrawEllipse(null, _outlinePen, pos, radius, radius);

        // 3D highlight overlay
        var highlightCenter = new Point(pos.X - radius * 0.25, pos.Y - radius * 0.25);
        dc.DrawEllipse(_highlightBrush, null, highlightCenter, radius * 0.6, radius * 0.6);
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Create a wedge/pie-slice geometry for a beach ball stripe.
    /// </summary>
    private static PathGeometry CreateWedgeGeometry(Point center, double radius, double startAngleDeg, double angleDeg)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = center };

        double startAngle = startAngleDeg * Math.PI / 180.0;
        double endAngle = (startAngleDeg + angleDeg) * Math.PI / 180.0;

        var startPoint = new Point(
            center.X + radius * Math.Cos(startAngle),
            center.Y + radius * Math.Sin(startAngle));

        var endPoint = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        figure.Segments.Add(new LineSegment(startPoint, true));
        figure.Segments.Add(new ArcSegment(
            endPoint,
            new Size(radius, radius),
            0,
            angleDeg > 180,
            SweepDirection.Clockwise,
            true));
        figure.Segments.Add(new LineSegment(center, true));

        figure.IsClosed = true;
        geometry.Figures.Add(figure);
        geometry.Freeze();

        return geometry;
    }
    #endregion
}
