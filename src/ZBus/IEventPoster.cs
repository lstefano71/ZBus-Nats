using ZFormat;

namespace ZBus;

/// <summary>
/// Allows adapters to post events into the kernel's waitpoint tree.
/// Thread-safe — adapters may call from any thread.
/// </summary>
public interface IEventPoster
{
    /// <summary>
    /// Post an event with general delivery.
    /// The event is delivered to the deepest waitpoint with an active waiter,
    /// bubbling up from objectName toward the root. If no waiter is active,
    /// queues at the target waitpoint.
    /// </summary>
    void PostEvent(string objectName, string eventType, ZValue data);

    /// <summary>
    /// Post an event with targeted delivery.
    /// The event is only visible to a waiter on the exact matching waitpoint.
    /// Never bubbles. Used for request/reply mailboxes.
    /// </summary>
    void PostTargeted(string objectName, string eventType, ZValue data);
}
