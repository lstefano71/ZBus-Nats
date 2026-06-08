using System.Collections.Concurrent;

namespace ZBus;

/// <summary>
/// Global registry of roots and adapter factories.
/// Static surface is minimal — just root lookup and adapter factory registration.
/// Each Root is an independent instance with its own object tree and waitpoints.
/// </summary>
public static class Bus
{
    private static readonly ConcurrentDictionary<string, Root> _roots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Func<IAdapter>> _adapterFactories = new();

    /// <summary>
    /// Register an adapter factory. Called once in [ModuleInitializer].
    /// Each root created afterwards will get its own adapter instance from this factory.
    /// </summary>
    public static void RegisterAdapterFactory(Func<IAdapter> factory)
    {
        _adapterFactories.Add(factory);
    }

    /// <summary>
    /// Get-or-create a root by name. Idempotent.
    /// If name is empty, auto-generates a unique name.
    /// </summary>
    public static (int rc, Root root) Init(string name)
    {
        if (string.IsNullOrEmpty(name))
            name = GenerateRootName();

        var root = _roots.GetOrAdd(name, n =>
        {
            var r = new Root(n);
            foreach (var factory in _adapterFactories)
            {
                var adapter = factory();
                r.RegisterAdapter(adapter);
            }
            return r;
        });

        return (ReturnCodes.OK, root);
    }

    /// <summary>
    /// Find the root for a given dotted name (first segment).
    /// Returns null if not found.
    /// </summary>
    public static Root? FindRoot(string dottedName)
    {
        var rootName = ExtractRootSegment(dottedName);
        _roots.TryGetValue(rootName, out var root);
        return root;
    }

    /// <summary>
    /// Remove and dispose a root. Called by zbus_close on a root name.
    /// </summary>
    public static bool RemoveRoot(string name)
    {
        if (_roots.TryRemove(name, out var root))
        {
            root.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Extract the first segment (root name) from a dotted path.
    /// "MyBus.N1.prices" → "MyBus"
    /// </summary>
    public static string ExtractRootSegment(string dottedName)
    {
        var dotIndex = dottedName.IndexOf('.');
        return dotIndex < 0 ? dottedName : dottedName[..dotIndex];
    }

    private static int _autoCounter;
    private static string GenerateRootName()
    {
        var n = Interlocked.Increment(ref _autoCounter);
        return $"ZBus{n:D8}";
    }

    // For testing — clears all roots and factories
    internal static void Reset()
    {
        foreach (var root in _roots.Values)
            root.Dispose();
        _roots.Clear();
        _adapterFactories.Clear();
        _autoCounter = 0;
    }
}
