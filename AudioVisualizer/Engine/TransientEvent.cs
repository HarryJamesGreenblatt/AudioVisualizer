using System;
using System.Windows;

namespace AudioVisualizer.Engine;

/// <summary>
/// Event fired from the audio thread when a transient (sharp attack) is detected.
/// Deferred via <see cref="EventQueue{T}"/> and consumed on the UI thread to spawn particle bursts.
/// </summary>
/// <param name="Band">Frequency band index where the transient was detected.</param>
/// <param name="Intensity">Magnitude of the transient (0–1 normalized).</param>
/// <param name="Position">World-space position for the resulting particle burst.</param>
public readonly record struct TransientEvent(
    int Band,
    float Intensity,
    Point Position);
