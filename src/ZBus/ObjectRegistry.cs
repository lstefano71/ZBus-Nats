using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ZBus;

/// <summary>
/// Thread-safe dotted-name object registry.
/// Case-insensitive. Enforces naming rules and parent-child relationships.
/// </summary>
public sealed partial class ObjectRegistry : IObjectRegistry
{
    private readonly ConcurrentDictionary<string, ObjectEntry> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootName;

    // Valid segment: letters, digits, underscore, hyphen. No leading digit.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_\-]*$")]
    private static partial Regex ValidSegmentRegex();

    public ObjectRegistry(string rootName)
    {
        _rootName = rootName;
    }

    public void Register(string name, string parentName, string objectType, string adapterId)
    {
        if (!IsValidSegment(name))
            throw new ArgumentException($"Invalid object name segment: '{name}'");

        var fullName = string.IsNullOrEmpty(parentName) || parentName.Equals(_rootName, StringComparison.OrdinalIgnoreCase)
            ? $"{_rootName}.{name}"
            : $"{parentName}.{name}";

        var entry = new ObjectEntry
        {
            FullName = fullName,
            LeafName = name,
            ParentFullName = string.IsNullOrEmpty(parentName) ? _rootName : parentName,
            ObjectType = objectType,
            AdapterId = adapterId
        };

        if (!_objects.TryAdd(fullName, entry))
            throw new InvalidOperationException($"Name already in use: '{fullName}'");
    }

    public void Unregister(string fullName)
    {
        _objects.TryRemove(fullName, out _);

        // Also remove all children (cascade)
        var prefix = fullName + ".";
        foreach (var key in _objects.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _objects.TryRemove(key, out _);
        }
    }

    public bool Exists(string fullName)
    {
        if (fullName.Equals(_rootName, StringComparison.OrdinalIgnoreCase))
            return true;
        return _objects.ContainsKey(fullName);
    }

    public IReadOnlyList<string> Children(string parentName)
    {
        var results = new List<string>();
        var prefix = parentName + ".";

        foreach (var kvp in _objects)
        {
            if (kvp.Value.ParentFullName.Equals(parentName, StringComparison.OrdinalIgnoreCase))
                results.Add(kvp.Value.FullName);
        }

        return results;
    }

    /// <summary>
    /// Get the entry for a full name. Returns null for root or not-found.
    /// </summary>
    public ObjectEntry? GetEntry(string fullName)
    {
        _objects.TryGetValue(fullName, out var entry);
        return entry;
    }

    /// <summary>
    /// Get all entries (for enumeration/shutdown).
    /// </summary>
    public IEnumerable<ObjectEntry> AllEntries() => _objects.Values;

    private static bool IsValidSegment(string segment)
    {
        return !string.IsNullOrEmpty(segment) && ValidSegmentRegex().IsMatch(segment);
    }
}
