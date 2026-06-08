using ZFormat;

namespace ZBus;

/// <summary>
/// Contract that every ZBus adapter must implement.
/// Adapters are registered at compile time and provide domain-specific
/// object types and verb exports (e.g., NATS subscribe, TCP connect).
/// </summary>
public interface IAdapter
{
    /// <summary>
    /// Unique identifier for this adapter (e.g., "nats", "tcp", "echo").
    /// Used as the prefix in adapter-specific export names.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Called once when the adapter is registered with a root.
    /// The adapter receives references to the kernel services it needs.
    /// </summary>
    void Initialize(IEventPoster poster, IObjectRegistry registry);

    /// <summary>
    /// Called when Close is issued on an object owned by this adapter.
    /// The adapter performs its own cleanup; the kernel handles the
    /// synthetic Closed event and registry removal.
    /// </summary>
    void CloseObject(string objectName);

    /// <summary>
    /// Get a property value from an adapter-owned object.
    /// Returns null if the property is not recognized.
    /// </summary>
    ZValue? GetProperty(string objectName, string propertyName);

    /// <summary>
    /// Set a property value on an adapter-owned object.
    /// Returns true if the property was accepted, false if not recognized.
    /// </summary>
    bool SetProperty(string objectName, string propertyName, ZValue value);
}
