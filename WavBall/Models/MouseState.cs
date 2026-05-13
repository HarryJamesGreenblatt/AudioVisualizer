using System.Windows;

namespace WavBall.Models;

/// <summary>
/// Plain-data snapshot of the mouse for one tick. Owned by <see cref="Scene"/>,
/// written by the WPF host (<c>VisualizerElement</c>) from mouse events, and read
/// by <see cref="Components.Input"/> instances each tick.
///
/// Engine code never references WPF — this struct is the entire interaction surface.
/// </summary>
public struct MouseState
{
    /// <summary>Cursor position in viewport coordinates (matches entity Position space).</summary>
    public Point Position;

    /// <summary>True while any mouse button is held.</summary>
    public bool IsDown;

    /// <summary>True for exactly one tick when the button transitions up→down.</summary>
    public bool JustPressed;

    /// <summary>True for exactly one tick when the button transitions down→up.</summary>
    public bool JustReleased;
}
