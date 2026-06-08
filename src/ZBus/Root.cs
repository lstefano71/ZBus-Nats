using System.Collections.Concurrent;
using ZFormat;

namespace ZBus;

/// <summary>
/// A root instance — owns an object registry, waitpoint tree, and adapter set.
/// Created via Bus.Init(). Independent of other roots.
/// </summary>
public sealed class Root : IDisposable
{
    private readonly ConcurrentDictionary<string, IAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public ObjectRegistry Registry { get; }
    internal WaitpointTree Waitpoints { get; }
    internal EventPoster Poster { get; }

    public Root(string name)
    {
        Name = name;
        Registry = new ObjectRegistry(name);
        Waitpoints = new WaitpointTree(name);
        Poster = new EventPoster(Waitpoints);
    }

    public string Name { get; }

    /// <summary>
    /// Register an adapter instance with this root.
    /// Called during root creation by Bus.Init().
    /// </summary>
    public void RegisterAdapter(IAdapter adapter)
    {
        if (!_adapters.TryAdd(adapter.Id, adapter))
            throw new InvalidOperationException($"Adapter '{adapter.Id}' already registered on root '{Name}'");

        adapter.Initialize(Poster, Registry);
    }

    /// <summary>
    /// Get a registered adapter by type.
    /// </summary>
    public TAdapter? GetAdapter<TAdapter>() where TAdapter : class, IAdapter
    {
        foreach (var adapter in _adapters.Values)
        {
            if (adapter is TAdapter typed)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Get a registered adapter by ID.
    /// </summary>
    public IAdapter? GetAdapter(string adapterId)
    {
        _adapters.TryGetValue(adapterId, out var adapter);
        return adapter;
    }

    /// <summary>
    /// Wait for an event at the given name (or this root).
    /// Blocking — meant to be called from an OS thread (⎕NA &amp;).
    /// </summary>
    public BusEvent? Wait(string name, int timeoutMs)
    {
        return Waitpoints.Wait(name, timeoutMs);
    }

    /// <summary>
    /// Close an object (or the root itself). Posts synthetic Closed event, cascades to children.
    /// </summary>
    public void Close(string fullName)
    {
        if (fullName.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            // Closing the root — shut everything down
            Dispose();
            return;
        }

        var entry = Registry.GetEntry(fullName);
        if (entry == null) return;

        // Notify the adapter
        var adapter = GetAdapter(entry.AdapterId);
        adapter?.CloseObject(fullName);

        // Post synthetic Closed event (general delivery — bubbles)
        Poster.PostEvent(fullName, "Closed", ZValue.EmptyNumeric);

        // Remove from registry and waitpoint tree
        Registry.Unregister(fullName);
        Waitpoints.Remove(fullName);
    }

    /// <summary>
    /// Get property — dispatches to adapter.
    /// </summary>
    public ZValue? GetProperty(string fullName, string propName)
    {
        // Kernel-owned properties for children
        if (propName.Equals("Children", StringComparison.OrdinalIgnoreCase))
        {
            var children = Registry.Children(fullName);
            return ZValue.FromStringArray(children);
        }

        // For root-level queries, try adapters first (they know connection state)
        if (fullName.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var adapter in _adapters.Values)
            {
                var result = adapter.GetProperty(fullName, propName);
                if (result != null) return result;
            }
            // Kernel fallback for State
            if (propName.Equals("State", StringComparison.OrdinalIgnoreCase))
                return ZValue.FromChars("Started");
            return null;
        }

        // Kernel-owned properties for registered objects
        if (propName.Equals("State", StringComparison.OrdinalIgnoreCase))
            return ZValue.FromChars(Registry.Exists(fullName) ? "Started" : "Unknown");
        if (propName.Equals("ObjectType", StringComparison.OrdinalIgnoreCase))
        {
            var e = Registry.GetEntry(fullName);
            return e != null ? ZValue.FromChars(e.ObjectType) : null;
        }

        // Dispatch to adapter
        var entry = Registry.GetEntry(fullName);
        if (entry == null) return null;
        var adapter2 = GetAdapter(entry.AdapterId);
        return adapter2?.GetProperty(fullName, propName);
    }

    /// <summary>
    /// Describe an object — dispatches to adapter.
    /// </summary>
    public ZValue? Describe(string fullName)
    {
        // Root-level describe: try each adapter
        if (fullName.Equals(Name, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var adapter in _adapters.Values)
            {
                var result = adapter.Describe(fullName);
                if (result != null) return result;
            }
            return null;
        }

        var entry = Registry.GetEntry(fullName);
        if (entry == null) return null;
        var adapter2 = GetAdapter(entry.AdapterId);
        return adapter2?.Describe(fullName);
    }

    /// <summary>
    /// Set property — dispatches to adapter.
    /// </summary>
    public bool SetProperty(string fullName, string propName, ZValue value)
    {
        var entry = Registry.GetEntry(fullName);
        if (entry == null) return false;
        var adapter = GetAdapter(entry.AdapterId);
        return adapter?.SetProperty(fullName, propName, value) ?? false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Close all objects
        foreach (var entry in Registry.AllEntries())
        {
            var adapter = GetAdapter(entry.AdapterId);
            adapter?.CloseObject(entry.FullName);
        }

        foreach (var adapter in _adapters.Values)
        {
            if (adapter is IDisposable d)
                d.Dispose();
        }
        _adapters.Clear();
        Waitpoints.Dispose();
    }
}

