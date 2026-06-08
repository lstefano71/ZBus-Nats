namespace ZBus;

/// <summary>
/// Allows adapters to register and unregister objects in the kernel's
/// dotted-name hierarchy.
/// Thread-safe — adapters may call from any thread.
/// </summary>
public interface IObjectRegistry
{
    /// <summary>
    /// Register a new object as a child of parentName.
    /// The full dotted name becomes parentName.name (or just name if parentName is the root).
    /// </summary>
    /// <param name="name">Leaf name of the object (no dots).</param>
    /// <param name="parentName">Dotted name of the parent, or empty string for root-level.</param>
    /// <param name="objectType">Adapter-defined type label (e.g., "Subscription", "Connection").</param>
    /// <param name="adapterId">The adapter that owns this object.</param>
    void Register(string name, string parentName, string objectType, string adapterId);

    /// <summary>
    /// Remove an object from the registry.
    /// The kernel will post a synthetic Closed event before removal.
    /// </summary>
    void Unregister(string fullName);

    /// <summary>
    /// Check whether a dotted name exists in the registry.
    /// </summary>
    bool Exists(string fullName);

    /// <summary>
    /// List immediate children of the given dotted name.
    /// </summary>
    IReadOnlyList<string> Children(string parentName);
}
