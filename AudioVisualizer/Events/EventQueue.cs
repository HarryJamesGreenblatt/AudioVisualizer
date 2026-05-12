using System;
using System.Collections.Concurrent;

namespace AudioVisualizer;

/// <summary>
/// Event Queue pattern: thread-safe queue for deferred cross-thread events.
/// Audio thread enqueues; UI thread drains at a safe point in the frame.
/// Wraps ConcurrentQueue for lock-free MPSC (multiple producer, single consumer).
/// </summary>
public sealed class EventQueue<T>
{
    #region Fields
    /// <summary>
    /// Lock-free concurrent queue backing the event buffer.
    /// </summary>
    private readonly ConcurrentQueue<T> _queue = new();
    #endregion

    #region Properties
    /// <summary>
    /// Number of pending events (approximate, for diagnostics only).
    /// </summary>
    public int Count => _queue.Count;
    #endregion

    #region Methods
    /// <summary>
    /// Enqueue an event. Safe to call from any thread (lock-free).
    /// </summary>
    /// <param name="item">The event to enqueue.</param>
    public void Enqueue(T item) => _queue.Enqueue(item);

    /// <summary>
    /// Try to dequeue a single event from the front of the queue.
    /// </summary>
    /// <param name="item">The dequeued event, or default if empty.</param>
    /// <returns>True if an event was dequeued; false if queue was empty.</returns>
    public bool TryDequeue(out T? item) => _queue.TryDequeue(out item);

    /// <summary>
    /// Drain all pending events, invoking the handler for each in FIFO order.
    /// Typically called once per frame at a safe point before physics.
    /// </summary>
    /// <param name="handler">Action invoked for each dequeued event.</param>
    public void DrainAll(Action<T> handler)
    {
        while (_queue.TryDequeue(out var item))
            handler(item);
    }
    #endregion
}
