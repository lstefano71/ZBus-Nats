using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ZBus;

/// <summary>
/// A waitpoint is a named event queue that supports blocking dequeue with timeout.
/// Uses two channels: one for general events (visible to ancestor scans) and
/// one for targeted events (exact-match only, never scanned by ancestors).
/// </summary>
internal sealed class Waitpoint : IDisposable
{
    private readonly Channel<BusEvent> _generalChannel;
    private readonly Channel<BusEvent> _targetedChannel;
    private volatile bool _disposed;
    private volatile bool _hasActiveWaiter;

    /// <summary>Maximum queue depth before discard-oldest kicks in.</summary>
    public int MaxQueueDepth { get; }

    public Waitpoint(int maxQueueDepth = 1024)
    {
        MaxQueueDepth = maxQueueDepth;
        var opts = new BoundedChannelOptions(maxQueueDepth)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        };
        _generalChannel = Channel.CreateBounded<BusEvent>(opts);
        _targetedChannel = Channel.CreateBounded<BusEvent>(opts);
    }

    /// <summary>True if a thread is currently blocked inside TryReceive.</summary>
    public bool HasActiveWaiter => _hasActiveWaiter;

    /// <summary>
    /// Enqueue a general event. Non-blocking. Returns false if disposed.
    /// </summary>
    public bool Post(BusEvent evt)
    {
        if (_disposed) return false;
        return _generalChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Enqueue a targeted event. Non-blocking. Returns false if disposed.
    /// Targeted events are only visible to exact-match Wait, never to ancestor scans.
    /// </summary>
    public bool PostTargeted(BusEvent evt)
    {
        if (_disposed) return false;
        return _targetedChannel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Non-blocking read of a single buffered general event.
    /// Does NOT set HasActiveWaiter. Used by ancestor descendant scans.
    /// </summary>
    public BusEvent? TryReadOneGeneral()
    {
        if (_disposed) return null;
        return _generalChannel.Reader.TryRead(out var evt) ? evt : null;
    }

    /// <summary>
    /// Block until an event is available (from either channel) or timeout expires.
    /// Checks targeted first (higher priority), then general.
    /// Returns null on timeout.
    /// </summary>
    public BusEvent? TryReceive(int timeoutMs)
    {
        if (_disposed) return null;

        // Fast path: check targeted first, then general
        if (_targetedChannel.Reader.TryRead(out var targeted))
            return targeted;
        if (_generalChannel.Reader.TryRead(out var general))
            return general;

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
                // Check both channels
                if (_targetedChannel.Reader.TryRead(out var t))
                    return t;
                if (_generalChannel.Reader.TryRead(out var g))
                    return g;

                // Wait on both — whichever signals first
                var targetedWait = _targetedChannel.Reader.WaitToReadAsync(token);
                var generalWait = _generalChannel.Reader.WaitToReadAsync(token);

                // If either is synchronously ready, loop back
                if (targetedWait.IsCompletedSuccessfully && targetedWait.Result)
                    continue;
                if (generalWait.IsCompletedSuccessfully && generalWait.Result)
                    continue;
                if (targetedWait.IsCompletedSuccessfully && !targetedWait.Result &&
                    generalWait.IsCompletedSuccessfully && !generalWait.Result)
                    return null; // both channels completed

                // Block until either channel has data
                var tTask = targetedWait.IsCompleted ? Task.FromResult(targetedWait.Result) : targetedWait.AsTask();
                var gTask = generalWait.IsCompleted ? Task.FromResult(generalWait.Result) : generalWait.AsTask();
                Task.WhenAny(tTask, gTask).GetAwaiter().GetResult();
                // Loop back to TryRead
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

    /// <summary>Number of general events currently buffered.</summary>
    public int GeneralCount => _generalChannel.Reader.Count;

    /// <summary>Number of targeted events currently buffered.</summary>
    public int TargetedCount => _targetedChannel.Reader.Count;

    /// <summary>Total events buffered.</summary>
    public int Count => GeneralCount + TargetedCount;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generalChannel.Writer.TryComplete();
        _targetedChannel.Writer.TryComplete();
    }
}
