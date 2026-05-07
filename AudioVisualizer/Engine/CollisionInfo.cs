using System.Windows;

namespace AudioVisualizer.Engine;

/// <summary>
/// Data carried by a collision notification (Observer pattern payload).
/// </summary>
/// <param name="ContactPoint">World-space point where the collision occurred.</param>
/// <param name="Normal">Surface normal at the contact point (points away from the collider).</param>
/// <param name="Impulse">Magnitude of the collision impulse (for effect scaling).</param>
public readonly record struct CollisionInfo(
    Point ContactPoint,
    Vector Normal,
    float Impulse);
