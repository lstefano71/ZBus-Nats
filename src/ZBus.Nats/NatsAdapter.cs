using System.Collections.Concurrent;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NATS.Client.ObjectStore;
using NATS.Client.Services;
using ZFormat;

namespace ZBus.Adapters.Nats;

/// <summary>
/// NATS adapter for ZBus. Owns the NatsConnection per root and manages
/// subscriptions, JetStream, KV, Object Store, and Services objects.
/// </summary>
public sealed class NatsAdapter : IAdapter, IDisposable
{
    private IEventPoster _poster = null!;
    private IObjectRegistry _registry = null!;
    private NatsConnection? _connection;
    private NatsJSContext? _js;
    private NatsKVContext? _kv;
    private NatsObjContext? _obj;
    private NatsSvcContext? _svc;
    private string _rootName = "";
    private volatile bool _disposed;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _subjectMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _objectTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INatsJSStream> _streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INatsJSConsumer> _consumers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INatsKVStore> _kvStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INatsObjStore> _objStores = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, INatsSvcServer> _services = new(StringComparer.OrdinalIgnoreCase);
    // Pending JsMsg keyed by seq number for ack/nak
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, INatsJSMsg<byte[]>>> _pendingAcks = new(StringComparer.OrdinalIgnoreCase);

    // Pre-connect options accumulated via SetProperty
    private string? _url;

    public string Id => "nats";

    public void Initialize(IEventPoster poster, IObjectRegistry registry)
    {
        _poster = poster;
        _registry = registry;
    }

    /// <summary>
    /// Post an event safely — swallows exceptions if adapter is disposed or poster is broken.
    /// Prevents unhandled exceptions from crossing into native code.
    /// </summary>
    private void SafePost(string objectName, string eventType, ZValue data)
    {
        if (_disposed) return;
        try { _poster.PostEvent(objectName, eventType, data); }
        catch { /* swallow — adapter is shutting down or poster is disposed */ }
    }

    /// <summary>
    /// Fire-and-forget a Task, ensuring any unhandled exception is swallowed.
    /// Prevents NativeAOT FailFast on unobserved task exceptions.
    /// </summary>
    private static void FireAndForget(Task task)
    {
        task.ContinueWith(static t =>
        {
            // Observe and swallow the exception to prevent UnobservedTaskException
            _ = t.Exception;
        }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    // ═══════════════════════════════════════════════════════════════
    // Connection
    // ═══════════════════════════════════════════════════════════════

    public int Connect(string rootName, string url)
    {
        if (_connection != null)
            return ReturnCodes.NameInUse;

        _rootName = rootName;
        _url = url;
        FireAndForget(ConnectAsync(rootName));
        return ReturnCodes.OK;
    }

    private async Task ConnectAsync(string rootName)
    {
        try
        {
            var opts = new NatsOpts { Url = _url! };
            var conn = new NatsConnection(opts);
            bool wasDisconnected = false;

            conn.ConnectionDisconnected += (_, _) =>
            {
                wasDisconnected = true;
                SafePost(rootName, "Disconnected", ZValue.EmptyChar);
                return ValueTask.CompletedTask;
            };
            conn.ConnectionOpened += (_, _) =>
            {
                // Only fire Reconnected if we previously disconnected
                if (wasDisconnected)
                {
                    wasDisconnected = false;
                    SafePost(rootName, "Reconnected", ZValue.EmptyChar);
                }
                return ValueTask.CompletedTask;
            };
            conn.ReconnectFailed += (_, _) =>
            {
                SafePost(rootName, "Error", ZValue.FromChars("Reconnect failed"));
                return ValueTask.CompletedTask;
            };

            await conn.ConnectAsync();
            _connection = conn;
            _js = new NatsJSContext(conn);
            _kv = new NatsKVContext(_js);
            _obj = new NatsObjContext(_js);
            _svc = new NatsSvcContext(conn);
            SafePost(rootName, "Connected", ZValue.EmptyChar);
        }
        catch (Exception ex)
        {
            SafePost(rootName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Publish
    // ═══════════════════════════════════════════════════════════════

    public async Task<int> PublishAsync(string subject, byte[] payload, NatsHeaders? headers = null)
    {
        if (_connection == null)
            return ReturnCodes.InvalidHandle;

        await _connection.PublishAsync(subject, payload, headers);
        return ReturnCodes.OK;
    }

    // ═══════════════════════════════════════════════════════════════
    // Subscribe
    // ═══════════════════════════════════════════════════════════════

    public string Subscribe(string rootName, string leafName, string subject, string? queueGroup)
    {
        var fullName = string.IsNullOrEmpty(leafName)
            ? $"{rootName}.sub_{Guid.NewGuid():N}"[..20]
            : $"{rootName}.{leafName}";

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "Subscription", Id);
        _subjectMap[fullName] = subject;
        _objectTypes[fullName] = "Subscription";

        var cts = new CancellationTokenSource();
        _subscriptions[fullName] = cts;

        FireAndForget(ConsumeLoop(fullName, subject, queueGroup, cts.Token));
        return fullName;
    }

    private async Task ConsumeLoop(string objectName, string subject, string? queueGroup, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _connection!.SubscribeAsync<byte[]>(subject, queueGroup, cancellationToken: ct))
            {
                var headersValue = ConvertHeaders(msg.Headers);
                var data = ZValue.Nested(
                    ZValue.FromChars(msg.Subject ?? ""),
                    ZValue.FromChars(msg.ReplyTo ?? ""),
                    msg.Data != null ? ZValue.FromBytes(msg.Data) : ZValue.EmptyNumeric,
                    headersValue
                );
                SafePost(objectName, "Msg", data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafePost(objectName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Request/Reply
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Send a request and create a mailbox object to receive the reply.
    /// The reply arrives as a "Reply" event on the mailbox object.
    /// Timeout → "Timeout" event. Auto-closes after one reply.
    /// </summary>
    public string Request(string rootName, string leafName, string subject, byte[] payload, NatsHeaders? headers, int timeoutMs)
    {
        if (_connection == null)
            return "";

        var fullName = string.IsNullOrEmpty(leafName)
            ? $"{rootName}.req_{Guid.NewGuid():N}"[..20]
            : $"{rootName}.{leafName}";

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "Request", Id);
        _subjectMap[fullName] = subject;
        _objectTypes[fullName] = "Request";

        var cts = new CancellationTokenSource();
        _subscriptions[fullName] = cts;

        FireAndForget(RequestLoop(fullName, subject, payload, headers, timeoutMs, cts.Token));
        return fullName;
    }

    private async Task RequestLoop(string objectName, string subject, byte[] payload, NatsHeaders? headers, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            var reply = await _connection!.RequestAsync<byte[], byte[]>(
                subject, payload, headers, cancellationToken: timeoutCts.Token);

            var headersValue = ConvertHeaders(reply.Headers);
            var data = ZValue.Nested(
                ZValue.FromChars(reply.Subject ?? ""),
                ZValue.FromChars(reply.ReplyTo ?? ""),
                reply.Data != null ? ZValue.FromBytes(reply.Data) : ZValue.EmptyNumeric,
                headersValue
            );
            SafePost(objectName, "Reply", data);
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested)
                SafePost(objectName, "Timeout", ZValue.EmptyNumeric);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("NoResponders") ||
                                    ex.Message.Contains("no responders", StringComparison.OrdinalIgnoreCase))
        {
            SafePost(objectName, "Timeout", ZValue.EmptyNumeric);
        }
        catch (Exception ex)
        {
            SafePost(objectName, "Error", ZValue.FromChars(ex.Message));
        }
        finally
        {
            // Auto-close after reply or timeout
            _subscriptions.TryRemove(objectName, out _);
            _subjectMap.TryRemove(objectName, out _);
            _objectTypes.TryRemove(objectName, out _);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // JetStream
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create or update a JetStream stream. Returns the full object name.
    /// </summary>
    public async Task<string> CreateStreamAsync(string rootName, string leafName, string streamName, string[] subjects)
    {
        if (_js == null) return "";

        var fullName = $"{rootName}.{leafName}";

        var config = new StreamConfig(streamName, subjects);
        var stream = await _js.CreateOrUpdateStreamAsync(config);

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "Stream", Id);
        _streams[fullName] = stream;
        _subjectMap[fullName] = string.Join(",", subjects);
        _objectTypes[fullName] = "Stream";

        return fullName;
    }

    /// <summary>
    /// Publish a message via JetStream (with ack). Returns (stream, seq) or error.
    /// </summary>
    public async Task<(int rc, string stream, long seq)> JsPublishAsync(string subject, byte[] payload, NatsHeaders? headers = null)
    {
        if (_js == null) return (ReturnCodes.InvalidHandle, "", 0);

        var ack = await _js.PublishAsync(subject, payload, headers: headers);
        if (ack.Error != null)
            return (ReturnCodes.InternalError, ack.Error.Description ?? "", 0);

        return (ReturnCodes.OK, ack.Stream ?? "", (long)ack.Seq);
    }

    /// <summary>
    /// Create or update a consumer on a stream. Starts a consume loop posting JsMsg events.
    /// </summary>
    public async Task<string> CreateConsumerAsync(string rootName, string streamFullName, string consumerName, string? filterSubject, string deliverPolicy)
    {
        if (_js == null) return "";

        // Find the stream object
        if (!_streams.TryGetValue(streamFullName, out var stream))
            return "";

        var fullName = $"{streamFullName}.{consumerName}";

        var config = new ConsumerConfig
        {
            Name = consumerName,
            DurableName = consumerName,
            FilterSubject = filterSubject,
            DeliverPolicy = deliverPolicy switch
            {
                "all" => ConsumerConfigDeliverPolicy.All,
                "last" => ConsumerConfigDeliverPolicy.Last,
                "new" => ConsumerConfigDeliverPolicy.New,
                "last_per_subject" => ConsumerConfigDeliverPolicy.LastPerSubject,
                _ => ConsumerConfigDeliverPolicy.All
            }
        };

        var consumer = await stream.CreateOrUpdateConsumerAsync(config);

        var registeredLeaf = consumerName;
        _registry.Register(registeredLeaf, streamFullName, "Consumer", Id);
        _consumers[fullName] = consumer;
        _objectTypes[fullName] = "Consumer";
        _pendingAcks[fullName] = new ConcurrentDictionary<long, INatsJSMsg<byte[]>>();

        var cts = new CancellationTokenSource();
        _subscriptions[fullName] = cts;

        FireAndForget(JsConsumeLoop(fullName, consumer, cts.Token));
        return fullName;
    }

    private async Task JsConsumeLoop(string objectName, INatsJSConsumer consumer, CancellationToken ct)
    {
        try
        {
            await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: ct))
            {
                long seq = (long)(msg.Metadata?.Sequence.Stream ?? 0);
                // Store for ack/nak
                if (_pendingAcks.TryGetValue(objectName, out var pending))
                    pending[seq] = msg;

                var headersValue = ConvertHeaders(msg.Headers);
                var data = ZValue.Nested(
                    ZValue.FromChars(msg.Subject ?? ""),
                    msg.Data != null ? ZValue.FromBytes(msg.Data) : ZValue.EmptyNumeric,
                    headersValue,
                    ZValue.FromInt(seq)
                );
                SafePost(objectName, "JsMsg", data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafePost(objectName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    /// <summary>
    /// Acknowledge a JetStream message by sequence number.
    /// </summary>
    public async Task<int> JsAckAsync(string consumerFullName, long seq)
    {
        if (!_pendingAcks.TryGetValue(consumerFullName, out var pending))
            return ReturnCodes.NotFound;

        if (!pending.TryRemove(seq, out var msg))
            return ReturnCodes.NotFound;

        await msg.AckAsync();
        return ReturnCodes.OK;
    }

    /// <summary>
    /// Negative-acknowledge a JetStream message (request redelivery).
    /// </summary>
    public async Task<int> JsNakAsync(string consumerFullName, long seq)
    {
        if (!_pendingAcks.TryGetValue(consumerFullName, out var pending))
            return ReturnCodes.NotFound;

        if (!pending.TryRemove(seq, out var msg))
            return ReturnCodes.NotFound;

        await msg.NakAsync();
        return ReturnCodes.OK;
    }

    // ═══════════════════════════════════════════════════════════════
    // Key/Value Store
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create or open a KV bucket. Returns full object name.
    /// </summary>
    public async Task<string> KvCreateBucketAsync(string rootName, string bucketName)
    {
        if (_kv == null) return "";

        var fullName = $"{rootName}.{bucketName}";
        var store = await _kv.CreateOrUpdateStoreAsync(new NatsKVConfig(bucketName));

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "KVBucket", Id);
        _kvStores[fullName] = store;
        _objectTypes[fullName] = "KVBucket";

        return fullName;
    }

    /// <summary>
    /// Get a value from KV. Returns (rc, value, revision).
    /// </summary>
    public async Task<(int rc, byte[]? value, long revision)> KvGetAsync(string bucketFullName, string key)
    {
        if (!_kvStores.TryGetValue(bucketFullName, out var store))
            return (ReturnCodes.NotFound, null, 0);

        try
        {
            var entry = await store.GetEntryAsync<byte[]>(key);
            if (entry.Value == null)
                return (ReturnCodes.NotFound, null, 0);

            return (ReturnCodes.OK, entry.Value, (long)entry.Revision);
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("KeyDeleted") ||
                                    ex.GetType().Name.Contains("KeyNotFound") ||
                                    ex.GetType().Name.Contains("NatsKV"))
        {
            return (ReturnCodes.NotFound, null, 0);
        }
    }

    /// <summary>
    /// Put a value into KV. Returns the revision.
    /// </summary>
    public async Task<(int rc, long revision)> KvPutAsync(string bucketFullName, string key, byte[] value)
    {
        if (!_kvStores.TryGetValue(bucketFullName, out var store))
            return (ReturnCodes.NotFound, 0);

        var rev = await store.PutAsync(key, value);
        return (ReturnCodes.OK, (long)rev);
    }

    /// <summary>
    /// Delete a key from KV.
    /// </summary>
    public async Task<int> KvDeleteAsync(string bucketFullName, string key)
    {
        if (!_kvStores.TryGetValue(bucketFullName, out var store))
            return ReturnCodes.NotFound;

        await store.DeleteAsync(key);
        return ReturnCodes.OK;
    }

    /// <summary>
    /// Watch a key pattern in KV. Posts KeyVal events.
    /// </summary>
    public string KvWatch(string bucketFullName, string keyPattern)
    {
        if (!_kvStores.TryGetValue(bucketFullName, out var store))
            return "";

        var watchName = $"{bucketFullName}.watch";
        var cts = new CancellationTokenSource();
        _subscriptions[watchName] = cts;
        _objectTypes[watchName] = "KVWatch";

        FireAndForget(KvWatchLoop(watchName, store, keyPattern, cts.Token));
        return watchName;
    }

    private async Task KvWatchLoop(string objectName, INatsKVStore store, string keyPattern, CancellationToken ct)
    {
        try
        {
            await foreach (var entry in store.WatchAsync<byte[]>(keyPattern, cancellationToken: ct))
            {
                var opStr = entry.Operation.ToString();
                var data = ZValue.Nested(
                    ZValue.FromChars(entry.Key),
                    entry.Value != null ? ZValue.FromBytes(entry.Value) : ZValue.EmptyNumeric,
                    ZValue.FromInt((long)entry.Revision),
                    ZValue.FromChars(opStr)
                );
                SafePost(objectName, "KeyVal", data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafePost(objectName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Object Store
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ObjCreateStoreAsync(string rootName, string storeName)
    {
        if (_obj == null) return "";

        var fullName = $"{rootName}.{storeName}";
        var store = await _obj.CreateObjectStoreAsync(storeName);

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "ObjStore", Id);
        _objStores[fullName] = store;
        _objectTypes[fullName] = "ObjStore";

        return fullName;
    }

    public async Task<(int rc, byte[]? data)> ObjGetAsync(string storeFullName, string objName)
    {
        if (!_objStores.TryGetValue(storeFullName, out var store))
            return (ReturnCodes.NotFound, null);

        try
        {
            var data = await store.GetBytesAsync(objName);
            return (ReturnCodes.OK, data);
        }
        catch
        {
            return (ReturnCodes.NotFound, null);
        }
    }

    public async Task<int> ObjPutAsync(string storeFullName, string objName, byte[] data)
    {
        if (!_objStores.TryGetValue(storeFullName, out var store))
            return ReturnCodes.NotFound;

        await store.PutAsync(objName, data);
        return ReturnCodes.OK;
    }

    public async Task<int> ObjDeleteAsync(string storeFullName, string objName)
    {
        if (!_objStores.TryGetValue(storeFullName, out var store))
            return ReturnCodes.NotFound;

        await store.DeleteAsync(objName);
        return ReturnCodes.OK;
    }

    public string ObjWatch(string storeFullName)
    {
        if (!_objStores.TryGetValue(storeFullName, out var store))
            return "";

        var watchName = $"{storeFullName}.watch";
        var cts = new CancellationTokenSource();
        _subscriptions[watchName] = cts;
        _objectTypes[watchName] = "ObjWatch";

        FireAndForget(ObjWatchLoop(watchName, store, cts.Token));
        return watchName;
    }

    private async Task ObjWatchLoop(string objectName, INatsObjStore store, CancellationToken ct)
    {
        try
        {
            await foreach (var info in store.WatchAsync(cancellationToken: ct))
            {
                var data = ZValue.Nested(
                    ZValue.FromChars(info.Name),
                    ZValue.FromInt((long)info.Size),
                    ZValue.FromChars(info.Deleted ? "Delete" : "Put")
                );
                SafePost(objectName, "ObjChanged", data);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SafePost(objectName, "Error", ZValue.FromChars(ex.Message));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Services
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> CreateServiceAsync(string rootName, string serviceName, string description, string version)
    {
        if (_svc == null) return "";

        var fullName = $"{rootName}.{serviceName}";
        var server = await _svc.AddServiceAsync(new NatsSvcConfig(serviceName, version)
        {
            Description = description
        });

        var registeredLeaf = fullName[(rootName.Length + 1)..];
        _registry.Register(registeredLeaf, rootName, "Service", Id);
        _services[fullName] = server;
        _objectTypes[fullName] = "Service";

        return fullName;
    }

    public async Task<int> AddEndpointAsync(string serviceFullName, string endpointName, string subject)
    {
        if (!_services.TryGetValue(serviceFullName, out var server))
            return ReturnCodes.NotFound;

        await server.AddEndpointAsync<byte[]>(
            handler: async msg =>
            {
                var headersValue = ConvertHeaders(msg.Headers);
                var data = ZValue.Nested(
                    ZValue.FromChars(msg.Subject ?? ""),
                    ZValue.FromChars(msg.ReplyTo ?? ""),
                    msg.Data != null ? ZValue.FromBytes(msg.Data) : ZValue.EmptyNumeric,
                    headersValue
                );
                SafePost(serviceFullName, "Request", data);

                // The APL side handles this by publishing to replyTo
                // We don't auto-reply here — the handler just posts the event
            },
            name: endpointName,
            subject: subject);

        return ReturnCodes.OK;
    }

    /// <summary>
    /// Discover services by pinging the NATS micro service protocol.
    /// Returns a nested Z array of (name, id, version) per responding service.
    /// </summary>
    public async Task<ZValue> DiscoverServicesAsync(string? serviceNameFilter, int timeoutMs)
    {
        if (_connection == null)
            return ZValue.EmptyNumeric;

        // NATS micro protocol: request on $SRV.PING or $SRV.PING.<name>
        var subject = string.IsNullOrEmpty(serviceNameFilter)
            ? "$SRV.PING"
            : $"$SRV.PING.{serviceNameFilter}";

        var results = new List<ZValue>();
        var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            await foreach (var msg in _connection.RequestManyAsync<byte[], byte[]>(
                subject, null, cancellationToken: cts.Token))
            {
                // Parse the JSON response to extract service info
                if (msg.Data != null)
                {
                    try
                    {
                        var json = System.Text.Encoding.UTF8.GetString(msg.Data);
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var id = root.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                        var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                        results.Add(ZValue.Nested(
                            ZValue.FromChars(name),
                            ZValue.FromChars(id),
                            ZValue.FromChars(version)
                        ));
                    }
                    catch { /* skip malformed responses */ }
                }
            }
        }
        catch (OperationCanceledException) { /* timeout — return what we have */ }

        if (results.Count == 0)
            return ZValue.EmptyNumeric;

        return ZValue.Nested(results.ToArray());
    }

    // ═══════════════════════════════════════════════════════════════
    // Describe
    // ═══════════════════════════════════════════════════════════════

    public ZValue? Describe(string objectName)
    {
        // Root-level describe: return adapter metadata (Conga convention)
        // [1] library identifier  [2] version  [3] state  [4] url
        if (objectName.Equals(_rootName, StringComparison.OrdinalIgnoreCase))
        {
            var state = _connection?.ConnectionState switch
            {
                NatsConnectionState.Open => "Connected",
                NatsConnectionState.Connecting => "Reconnecting",
                _ => "Disconnected"
            };
            return ZValue.Nested(
                ZValue.FromChars("[ZBus.Nats]"),
                ZValue.FromChars("NATS.Net 2.x / ZBus 1.0"),
                ZValue.FromChars(state ?? "Disconnected"),
                ZValue.FromChars(_url ?? "")
            );
        }

        // Child describe: [1] name  [2] type  [3] state  [4..] extras
        if (_subscriptions.ContainsKey(objectName) || _streams.ContainsKey(objectName) 
            || _kvStores.ContainsKey(objectName) || _objStores.ContainsKey(objectName)
            || _services.ContainsKey(objectName))
        {
            _subjectMap.TryGetValue(objectName, out var subj);
            _objectTypes.TryGetValue(objectName, out var objType);
            return ZValue.Nested(
                ZValue.FromChars(objectName),
                ZValue.FromChars(objType ?? "Unknown"),
                ZValue.FromChars("Started"),
                ZValue.FromChars(subj ?? "")
            );
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    // IAdapter
    // ═══════════════════════════════════════════════════════════════

    public void CloseObject(string objectName)
    {
        if (_subscriptions.TryRemove(objectName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
        _subjectMap.TryRemove(objectName, out _);
        _objectTypes.TryRemove(objectName, out _);
        _streams.TryRemove(objectName, out _);
        _consumers.TryRemove(objectName, out _);
        _pendingAcks.TryRemove(objectName, out _);
        _kvStores.TryRemove(objectName, out _);
        _objStores.TryRemove(objectName, out _);
        if (_services.TryRemove(objectName, out var svc))
            svc.StopAsync().GetAwaiter().GetResult();
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
        if (propertyName.Equals("Subject", StringComparison.OrdinalIgnoreCase))
        {
            _subjectMap.TryGetValue(objectName, out var subj);
            return subj != null ? ZValue.FromChars(subj) : null;
        }
        return null;
    }

    public bool SetProperty(string objectName, string propertyName, ZValue value)
    {
        // Pre-connect options accumulated here
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var cts in _subscriptions.Values)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
        _subscriptions.Clear();
        _subjectMap.Clear();
        _objectTypes.Clear();
        try { _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Convert NATS headers to an Nx2 nested matrix ZValue.
    /// Returns EmptyNumeric if no headers.
    /// </summary>
    private static ZValue ConvertHeaders(NatsHeaders? headers)
    {
        if (headers == null || headers.Count == 0)
            return ZValue.EmptyNumeric;

        var items = new List<ZValue>();
        foreach (var kvp in headers)
        {
            // Each header can have multiple values; flatten to one row per value
            foreach (var val in kvp.Value)
            {
                items.Add(ZValue.FromChars(kvp.Key));
                items.Add(ZValue.FromChars(val ?? ""));
            }
        }

        int rows = items.Count / 2;
        return ZValue.Nested([rows, 2], items.ToArray());
    }

    /// <summary>
    /// Convert an Nx2 nested matrix ZValue to NatsHeaders.
    /// Returns null if the value is empty/non-nested.
    /// </summary>
    internal static NatsHeaders? ParseHeaders(ZValue value)
    {
        if (value.Type != ZType.Nested || value.Children.Length == 0)
            return null;

        var headers = new NatsHeaders();
        var children = value.Children;
        // Nx2 matrix: children are laid out row-major
        int count = children.Length / 2;
        for (int i = 0; i < count; i++)
        {
            var key = children[i * 2].AsString();
            var val = children[i * 2 + 1].AsString();
            headers.Add(key, val);
        }
        return headers;
    }
}
