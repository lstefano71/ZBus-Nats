using System.Collections.Concurrent;
using NATS.Client.Core;
using ZFormat;

namespace ZBus.Adapters.Nats;

/// <summary>
/// NATS adapter for ZBus. Owns the NatsClient connection per root and manages
/// subscriptions, JetStream, KV, Object Store, and Services objects.
/// </summary>
public sealed class NatsAdapter : IAdapter, IDisposable
{
    private IEventPoster _poster = null!;
    private IObjectRegistry _registry = null!;
    private NatsConnection? _connection;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    // Pre-connect options accumulated via SetProperty
    private string? _url;

    public string Id => "nats";

    public void Initialize(IEventPoster poster, IObjectRegistry registry)
    {
        _poster = poster;
        _registry = registry;
    }

    /// <summary>
    /// Initiate a non-blocking connection to a NATS server.
    /// Posts Connected or Error event on the root when complete.
    /// </summary>
    public int Connect(string rootName, string url)
    {
        if (_connection != null)
            return ReturnCodes.NameInUse; // already connected

        _url = url;
        _ = ConnectAsync(rootName);
        return ReturnCodes.OK;
    }

    private async Task ConnectAsync(string rootName)
    {
        try
        {
            var opts = new NatsOpts { Url = _url! };
            var conn = new NatsConnection(opts);
            await conn.ConnectAsync();
            _connection = conn;
            _poster.PostEvent(rootName, "Connected", ZValue.EmptyChar);
        }
        catch (Exception ex)
        {
            _poster.PostEvent(rootName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    /// <summary>
    /// Publish a message. Payload is raw bytes.
    /// </summary>
    public async Task<int> PublishAsync(string subject, byte[] payload)
    {
        if (_connection == null)
            return ReturnCodes.InvalidHandle;

        await _connection.PublishAsync(subject, payload);
        return ReturnCodes.OK;
    }

    /// <summary>
    /// Subscribe to a subject. Creates an object in the registry and starts
    /// a background consume loop that posts Msg events.
    /// </summary>
    public int Subscribe(string rootName, string leafName, string subject, string? queueGroup)
    {
        if (_connection == null)
            return ReturnCodes.InvalidHandle;

        var fullName = string.IsNullOrEmpty(leafName)
            ? $"{rootName}.sub_{Guid.NewGuid():N}"[..20]
            : $"{rootName}.{leafName}";

        _registry.Register(leafName.Length > 0 ? leafName : fullName[(rootName.Length + 1)..], rootName, "Subscription", Id);

        var cts = new CancellationTokenSource();
        _subscriptions[fullName] = cts;

        _ = ConsumeLoop(fullName, subject, queueGroup, cts.Token);
        return ReturnCodes.OK;
    }

    /// <summary>Returns the full name of the last subscribed object (for >Z output).</summary>
    public string? LastCreatedName { get; private set; }

    private async Task ConsumeLoop(string objectName, string subject, string? queueGroup, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _connection!.SubscribeAsync<byte[]>(subject, queueGroup, cancellationToken: ct))
            {
                var data = ZValue.Nested(
                    ZValue.FromChars(msg.Subject ?? ""),
                    ZValue.FromChars(msg.ReplyTo ?? ""),
                    msg.Data != null ? ZValue.FromBytes(msg.Data) : ZValue.EmptyNumeric,
                    ZValue.EmptyNumeric // headers — TODO
                );
                _poster.PostEvent(objectName, "Msg", data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _poster.PostEvent(objectName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    public void CloseObject(string objectName)
    {
        if (_subscriptions.TryRemove(objectName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    public ZValue? GetProperty(string objectName, string propertyName)
    {
        if (propertyName.Equals("State", StringComparison.OrdinalIgnoreCase))
        {
            var state = _connection?.ConnectionState switch
            {
                NatsConnectionState.Open => "Connected",
                NatsConnectionState.Connecting => "Reconnecting",
                _ => "Disconnected"
            };
            return ZValue.FromChars(state ?? "Disconnected");
        }
        if (propertyName.Equals("Url", StringComparison.OrdinalIgnoreCase))
            return ZValue.FromChars(_url ?? "");
        return null;
    }

    public bool SetProperty(string objectName, string propertyName, ZValue value)
    {
        // Pre-connect options could be accumulated here
        return false;
    }

    public void Dispose()
    {
        foreach (var cts in _subscriptions.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _subscriptions.Clear();
        _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
