using System.Collections.Concurrent;
using ZFormat;

namespace ZBus.Adapters.Echo;

/// <summary>
/// Echo/Timer adapter. Creates timer objects that post Tick events,
/// and echo objects that reflect back whatever is sent to them.
/// </summary>
public sealed class EchoAdapter : IAdapter, IDisposable
{
    private IEventPoster _poster = null!;
    private IObjectRegistry _registry = null!;
    private readonly ConcurrentDictionary<string, TimerState> _timers = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "echo";

    public void Initialize(IEventPoster poster, IObjectRegistry registry)
    {
        _poster = poster;
        _registry = registry;
    }

    /// <summary>
    /// Create a timer object that posts Tick events at the given interval.
    /// </summary>
    public string CreateTimer(string parentName, string leafName, int intervalMs)
    {
        _registry.Register(leafName, parentName, "Timer", Id);
        var fullName = $"{parentName}.{leafName}";

        var state = new TimerState(fullName, _poster);
        state.Start(intervalMs);
        _timers[fullName] = state;

        return fullName;
    }

    /// <summary>
    /// Echo: post the input data back as a Receive event on the same object.
    /// </summary>
    public void Send(string objectName, ZValue data)
    {
        _poster.PostEvent(objectName, "Receive", data);
    }

    public void CloseObject(string objectName)
    {
        if (_timers.TryRemove(objectName, out var timer))
        {
            timer.Stop();
        }
    }

    public ZValue? GetProperty(string objectName, string propertyName)
    {
        if (_timers.TryGetValue(objectName, out var timer))
        {
            if (propertyName.Equals("Interval", StringComparison.OrdinalIgnoreCase))
                return ZValue.FromInt32(timer.IntervalMs);
            if (propertyName.Equals("TickCount", StringComparison.OrdinalIgnoreCase))
                return ZValue.FromInt32(timer.TickCount);
        }
        return null;
    }

    public bool SetProperty(string objectName, string propertyName, ZValue value)
    {
        // No settable properties for now
        return false;
    }

    public void Dispose()
    {
        foreach (var timer in _timers.Values)
            timer.Stop();
        _timers.Clear();
    }
}

/// <summary>
/// Internal state for a running timer.
/// </summary>
internal sealed class TimerState
{
    private readonly string _objectName;
    private readonly IEventPoster _poster;
    private Timer? _timer;
    private int _tickCount;

    public int IntervalMs { get; private set; }
    public int TickCount => _tickCount;

    public TimerState(string objectName, IEventPoster poster)
    {
        _objectName = objectName;
        _poster = poster;
    }

    public void Start(int intervalMs)
    {
        IntervalMs = intervalMs;
        _timer = new Timer(OnTick, null, intervalMs, intervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTick(object? state)
    {
        var count = Interlocked.Increment(ref _tickCount);
        _poster.PostEvent(_objectName, "Tick", ZValue.FromInt32(count));
    }
}
