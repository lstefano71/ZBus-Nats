using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ZBus;

/// <summary>
/// A waitpoint is a named event queue that supports blocking dequeue with timeout.
/// Uses Channel&lt;BusEvent&gt; internally (same pattern as Conga-Sharp CommandMailbox).
/// </summary>
internal sealed class Waitpoint : IDisposable
{
    private readonly Channel<BusEvent> _channel;
    private volatile bool _disposed;
    private volatile bool _hasActiveWaiter;

    /// <summary>Maximum queue depth before discard-oldest kicks in.</summary>
    public int MaxQueueDepth { get; }

    public Waitpoint(int maxQueueDepth = 1024)
    {
        MaxQueueDepth = maxQueueDepth;
        _channel = Channel.CreateBounded<BusEvent>(new BoundedChannelOptions(maxQueueDepth)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>True if a thread is currently blocked inside TryReceive.</summary>
    public bool HasActiveWaiter => _hasActiveWaiter;

    /// <summary>
    /// Enqueue an event. Non-blocking. Returns false if disposed.
    /// Uses DropOldest on overflow (bounded channel handles this).
    /// </summary>
    public bool Post(BusEvent evt)
    {
        if (_disposed) return false;
        return _channel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Block until an event is available or timeout expires.
    /// Returns null on timeout.
    /// </summary>
    public BusEvent? TryReceive(int timeoutMs)
    {
        if (_disposed) return null;

        // Fast path: event already buffered
        if (_channel.Reader.TryRead(out var immediate))
            return immediate;

        // Slow path: wait with timeout
        _hasActiveWaiter = true;
        CancellationTokenSource? cts = null;
        try
        {
            CancellationToken token;
            if (timeoutMs == Timeout.Infinite)
            {
                token = CancellationToken.None;
            }
            else
            {
                cts = new CancellationTokenSource(timeoutMs);
                token = cts.Token;
            }

            while (true)
            {
                if (_channel.Reader.TryRead(out var evt))
                    return evt;

                var waitTask = _channel.Reader.WaitToReadAsync(token);
                if (waitTask.IsCompletedSuccessfully)
                {
                    if (!waitTask.Result) return null; // channel completed
                    continue;
                }

                // Must block
                if (!waitTask.AsTask().GetAwaiter().GetResult())
                    return null;
            }
        }
        catch (OperationCanceledException)
        {
            return null; // timeout
        }
        catch (ChannelClosedException)
        {
            return null;
        }
        finally
        {
            _hasActiveWaiter = false;
            cts?.Dispose();
        }
    }

    /// <summary>Number of events currently buffered.</summary>
    public int Count => _channel.Reader.Count;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
    }
}
