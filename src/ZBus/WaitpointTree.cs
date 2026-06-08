using System.Collections.Concurrent;

namespace ZBus;

/// <summary>
/// Manages the tree of waitpoints, one per named object.
/// Handles hierarchical event delivery (most-specific-wins + bubbling).
/// </summary>
internal sealed class WaitpointTree : IDisposable
{
    private readonly ConcurrentDictionary<string, Waitpoint> _waitpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootName;

    public WaitpointTree(string rootName)
    {
        _rootName = rootName;
        // Root always has a waitpoint
        _waitpoints[rootName] = new Waitpoint();
    }

    /// <summary>
    /// Get or create a waitpoint for the given name.
    /// </summary>
    public Waitpoint GetOrCreate(string fullName)
    {
        return _waitpoints.GetOrAdd(fullName, _ => new Waitpoint());
    }

    /// <summary>
    /// Remove a waitpoint (on object close).
    /// </summary>
    public void Remove(string fullName)
    {
        if (_waitpoints.TryRemove(fullName, out var wp))
            wp.Dispose();
    }

    /// <summary>
    /// Post an event with general delivery.
    /// Finds the deepest waitpoint with an active waiter, bubbling up from targetName.
    /// If no active waiter, queues at the target.
    /// </summary>
    public void PostGeneral(string targetName, BusEvent evt)
    {
        // Walk from target up to root, looking for active waiter
        var current = targetName;
        while (true)
        {
            if (_waitpoints.TryGetValue(current, out var wp) && wp.HasActiveWaiter)
            {
                wp.Post(evt);
                return;
            }

            // Move to parent
            var dotIdx = current.LastIndexOf('.');
            if (dotIdx < 0) break;
            current = current[..dotIdx];
        }

        // No active waiter found anywhere — queue at root (always exists).
        // This ensures events bubble up and are picked up by the next Wait on root.
        _waitpoints[_rootName].Post(evt);
    }

    /// <summary>
    /// Post an event with targeted delivery.
    /// Only visible to exact-match waitpoint. Never bubbles.
    /// </summary>
    public void PostTargeted(string targetName, BusEvent evt)
    {
        GetOrCreate(targetName).Post(evt);
    }

    /// <summary>
    /// Block waiting for an event at the given name.
    /// </summary>
    public BusEvent? Wait(string name, int timeoutMs)
    {
        var wp = GetOrCreate(name);
        return wp.TryReceive(timeoutMs);
    }

    public void Dispose()
    {
        foreach (var wp in _waitpoints.Values)
            wp.Dispose();
        _waitpoints.Clear();
    }
}
