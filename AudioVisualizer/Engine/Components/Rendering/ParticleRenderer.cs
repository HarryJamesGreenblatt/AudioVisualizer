using System;
using System.Windows;
using System.Windows.Media;
using AudioVisualizer.Engine.Entities;

namespace AudioVisualizer.Engine.Components.Rendering;

/// <summary>
/// Render component for the particle pool entity.
/// Iterates the pool's struct buffer and draws each live particle as a fading dot.
/// Operates on the buffer directly to avoid per-particle dispatch overhead.
/// </summary>
public sealed class ParticleRenderer : IRenderingComponent
{
    #region Fields
    /// <summary>
    /// The pool entity whose particle buffer this component renders.
    /// </summary>
    private readonly ParticlePool _pool;
    #endregion

    #region Constructor
    /// <summary>
    /// Create a particle renderer operating on the given pool's buffer.
    /// </summary>
    /// <param name="pool">The pool entity whose particles are drawn.</param>
    public ParticleRenderer(ParticlePool pool)
    {
        _pool = pool;
    }
    #endregion

    #region Methods
    /// <inheritdoc />
    public void Render(SceneEntity entity, DrawingContext dc, Size viewport)
    {
        var buffer = _pool.Buffer;
        for (int i = 0; i < buffer.Length; i++)
        {
            ref var p = ref buffer[i];
            if (p.FramesLeft <= 0) continue;

            // Alpha fades with remaining lifetime
            byte alpha = (byte)Math.Clamp(p.FramesLeft * 8, 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B));
            brush.Freeze();
            dc.DrawEllipse(brush, null, p.Position, 2, 2);
        }
    }
    #endregion
}
