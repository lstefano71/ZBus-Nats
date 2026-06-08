using ZFormat;

namespace ZBus;

/// <summary>
/// Concrete implementation of IEventPoster, bound to a specific root's WaitpointTree.
/// Thread-safe — adapters call from any thread.
/// </summary>
internal sealed class EventPoster : IEventPoster
{
    private readonly WaitpointTree _tree;

    public EventPoster(WaitpointTree tree)
    {
        _tree = tree;
    }

    public void PostEvent(string objectName, string eventType, ZValue data)
    {
        var evt = new BusEvent(objectName, eventType, data, targeted: false);
        _tree.PostGeneral(objectName, evt);
    }

    public void PostTargeted(string objectName, string eventType, ZValue data)
    {
        var evt = new BusEvent(objectName, eventType, data, targeted: true);
        _tree.PostTargeted(objectName, evt);
    }
}
