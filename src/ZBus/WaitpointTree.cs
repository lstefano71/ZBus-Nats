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
    /// If there's an active waiter at the target or any ancestor, deliver directly.
    /// Otherwise, buffer at the target waitpoint (never at root — root scans children).
    /// </summary>
    public void PostGeneral(string targetName, BusEvent evt)
    {
        // Walk from target up to root, looking for active waiter (immediate delivery)
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

        // No active waiter anywhere — buffer at target.
        // Root Wait will find it by scanning children.
        GetOrCreate(targetName).Post(evt);
    }

    /// <summary>
    /// Post an event with targeted delivery.
    /// Only visible to exact-match waitpoint. Never bubbles. Never found by ancestor scans.
    /// </summary>
    public void PostTargeted(string targetName, BusEvent evt)
    {
        GetOrCreate(targetName).PostTargeted(evt);
    }

    /// <summary>
    /// Block waiting for an event at the given name.
    /// Leaf wait: reads from own channels (targeted + general).
    /// Ancestor wait: reads from own channels + scans descendant general channels.
    /// </summary>
    public BusEvent? Wait(string name, int timeoutMs)
    {
        var wp = GetOrCreate(name);

        // Fast path: check own waitpoint (targeted + general)
        var fast = wp.TryReceive(0);
        if (fast != null)
            return fast;

        // Check descendants for buffered general events
        var fromChild = TryDrainOneDescendant(name);
        if (fromChild != null)
            return fromChild;

        // Slow path: block on own channels with timeout, periodically scanning descendants
        return WaitWithDescendantScan(name, wp, timeoutMs);
    }

    /// <summary>
    /// Scan descendant waitpoints for a buffered general event.
    /// Targeted events are invisible to ancestor scans.
    /// Returns the first found, or null if none.
    /// </summary>
    private BusEvent? TryDrainOneDescendant(string ancestorName)
    {
        var prefix = ancestorName + ".";
        foreach (var kvp in _waitpoints)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (kvp.Value.TryReadOneGeneral() is { } evt)
                    return evt;
            }
        }
        return null;
    }

    /// <summary>
    /// Block on own channel, periodically waking to scan descendants.
    /// Uses short poll intervals to balance latency vs CPU.
    /// </summary>
    private BusEvent? WaitWithDescendantScan(string name, Waitpoint wp, int timeoutMs)
    {
        const int pollIntervalMs = 5;
        int elapsed = 0;

        while (elapsed < timeoutMs || timeoutMs == -1)
        {
            int slice = timeoutMs == -1
                ? pollIntervalMs
                : Math.Min(pollIntervalMs, timeoutMs - elapsed);

            var evt = wp.TryReceive(slice);
            if (evt != null) return evt;

            // Check descendants
            var fromChild = TryDrainOneDescendant(name);
            if (fromChild != null) return fromChild;

            elapsed += slice;
        }

        return null;
    }

    public void Dispose()
    {
        foreach (var wp in _waitpoints.Values)
            wp.Dispose();
        _waitpoints.Clear();
    }
}
