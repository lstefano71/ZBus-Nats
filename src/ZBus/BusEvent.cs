using ZFormat;

namespace ZBus;

/// <summary>
/// An event in the ZBus system. Immutable once created.
/// Corresponds to the 4-element APL tuple: (rc objectName eventType data).
/// </summary>
public sealed class BusEvent
{
    public BusEvent(string objectName, string eventType, ZValue data, bool targeted = false)
    {
        ObjectName = objectName;
        EventType = eventType;
        Data = data;
        Targeted = targeted;
    }

    /// <summary>Dotted name of the object that produced this event.</summary>
    public string ObjectName { get; }

    /// <summary>Event type label (e.g., "Receive", "Closed", "Timeout", "Tick").</summary>
    public string EventType { get; }

    /// <summary>Adapter-defined payload.</summary>
    public ZValue Data { get; }

    /// <summary>If true, only delivered to exact-match waitpoint (never bubbles).</summary>
    public bool Targeted { get; }
}
