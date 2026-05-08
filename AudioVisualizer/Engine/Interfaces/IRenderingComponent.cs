using System.Windows;
using System.Windows.Media;

namespace AudioVisualizer.Engine;

/// <summary>
/// Component responsible for drawing an entity to the screen.
/// Called once per render frame after all physics ticks are complete.
/// </summary>
public interface IRenderingComponent
{
    /// <summary>
    /// Draw the entity to the given context.
    /// </summary>
    /// <param name="entity">The owning entity providing position and state.</param>
    /// <param name="dc">WPF drawing context for immediate-mode rendering.</param>
    /// <param name="viewport">Current viewport dimensions.</param>
    void Render(SceneEntity entity, DrawingContext dc, Size viewport);
}
