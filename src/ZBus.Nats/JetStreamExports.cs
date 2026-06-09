using System.Runtime.InteropServices;
using ZBus;
using ZBus.Adapters.Nats;
using ZFormat;

/// <summary>
/// JetStream ⎕NA verb exports.
/// </summary>
public static unsafe class JetStreamExports
{
    /// <summary>
    /// Create or update a JetStream stream.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_stream &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc name) ← nats_stream 'N1' 'ORDERS' 'orders.>'
    /// APL: (rc name) ← nats_stream 'N1' 'ORDERS' ('orders.>' 'orders.new')
    /// APL: (rc name) ← nats_stream 'N1' 'ORDERS' ('orders.>' (2 2⍴'max_msgs' 100000 'storage' 'memory'))
    /// Input =Z: subject (char vector), subjects (nested vector), or (subjects, opts_Nx2).
    /// Opts keys: max_msgs, max_bytes, max_age_s, retention (limits/interest/workqueue), storage (file/memory)
    /// Output =Z: the full dotted name of the stream object.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_stream")]
    public static int ZBusNatsStream(nint rootNamePtr, nint streamNamePtr, nint* subjectsZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var streamName = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";

            // Read =Z input
            nint payloadPtr = *subjectsZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            // Parse subjects and optional config
            string[] subjects;
            long? maxMsgs = null;
            long? maxBytes = null;
            TimeSpan? maxAge = null;
            string? retention = null;
            string? storage = null;

            if (value.Type == ZType.Char)
            {
                // Simple: single subject string
                subjects = [value.AsString()];
            }
            else if (value.Type == ZType.Nested)
            {
                var children = value.Children;

                // Check if last child is an Nx2 options matrix (nested with pairs)
                // Heuristic: if last child is nested and its children are all 2-element nested vectors → options
                bool hasOpts = children.Length >= 2
                    && children[^1].Type == ZType.Nested
                    && children[^1].Children.Length > 0
                    && children[^1].Children[0].Type == ZType.Nested
                    && children[^1].Children[0].Children.Length == 2;

                if (hasOpts)
                {
                    // (subjects..., opts)
                    int subCount = children.Length - 1;
                    if (subCount == 1 && children[0].Type == ZType.Nested)
                    {
                        // ((sub1 sub2 ...), opts)
                        var subChildren = children[0].Children;
                        subjects = new string[subChildren.Length];
                        for (int i = 0; i < subChildren.Length; i++)
                            subjects[i] = subChildren[i].AsString();
                    }
                    else
                    {
                        // (sub1, opts) where sub1 is a char vector
                        subjects = new string[subCount];
                        for (int i = 0; i < subCount; i++)
                            subjects[i] = children[i].AsString();
                    }

                    // Parse options
                    var opts = children[^1].Children;
                    for (int i = 0; i < opts.Length; i++)
                    {
                        var pair = opts[i].Children;
                        var key = pair[0].AsString().ToLowerInvariant();
                        switch (key)
                        {
                            case "max_msgs": maxMsgs = (long)pair[1].AsDouble(); break;
                            case "max_bytes": maxBytes = (long)pair[1].AsDouble(); break;
                            case "max_age_s": maxAge = TimeSpan.FromSeconds(pair[1].AsDouble()); break;
                            case "retention": retention = pair[1].AsString().ToLowerInvariant(); break;
                            case "storage": storage = pair[1].AsString().ToLowerInvariant(); break;
                        }
                    }
                }
                else
                {
                    // Just multiple subjects
                    subjects = new string[children.Length];
                    for (int i = 0; i < children.Length; i++)
                        subjects[i] = children[i].AsString();
                }
            }
            else
            {
                subjects = [value.AsString()];
            }

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = adapter.CreateStreamAsync(rootName, streamName, streamName, subjects,
                        maxMsgs, maxBytes, maxAge, retention, storage)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(fullName))
                {
                    ZWriter.WriteToNative((nint)subjectsZ, ZValue.EmptyChar);
                    return ReturnCodes.InvalidHandle;
                }
                ZWriter.WriteToNative((nint)subjectsZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            NatsAdapter.RecordStaticError(root, ex, "zbus_nats_stream");
            ZWriter.WriteToNative((nint)subjectsZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Purge all messages from a JetStream stream.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_stream_purge &lt;0T1'
    /// APL: rc ← nats_stream_purge 'N1.ORDERS'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_stream_purge")]
    public static int ZBusNatsStreamPurge(nint streamNamePtr)
    {
        try
        {
            var streamFullName = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(streamFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.PurgeStreamAsync(streamFullName).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_stream_purge");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Delete a JetStream stream entirely.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_stream_delete &lt;0T1'
    /// APL: rc ← nats_stream_delete 'N1.ORDERS'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_stream_delete")]
    public static int ZBusNatsStreamDelete(nint streamNamePtr)
    {
        try
        {
            var streamFullName = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(streamFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.DeleteStreamAsync(streamFullName).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_stream_delete");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Publish a message via JetStream (acknowledged).
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_jspub &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc ack) ← nats_jspub 'N1' 'orders.new' payload
    /// Input =Z: payload or (payload, headers).
    /// Output =Z: ack info nested (stream, seq).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_jspub")]
    public static int ZBusNatsJsPub(nint rootNamePtr, nint subjectPtr, nint* dataZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var subject = Marshal.PtrToStringAnsi(subjectPtr) ?? "";

            // Read =Z input
            nint payloadPtr = *dataZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            byte[] payload;
            NATS.Client.Core.NatsHeaders? headers = null;
            if (value.Type == ZType.Nested && value.Children.Length == 2)
            {
                payload = ExtractPayloadBytes(value[0]);
                headers = NatsAdapter.ParseHeaders(value[1]);
            }
            else
            {
                payload = ExtractPayloadBytes(value);
            }

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var (rc, stream, seq) = adapter.JsPublishAsync(subject, payload, headers)
                    .GetAwaiter().GetResult();
                if (rc != ReturnCodes.OK)
                {
                    ZWriter.WriteToNative((nint)dataZ, ZValue.FromChars(stream)); // error desc
                    return rc;
                }
                var ackValue = ZValue.Nested(
                    ZValue.FromChars(stream),
                    ZValue.FromInt(seq)
                );
                ZWriter.WriteToNative((nint)dataZ, ackValue);
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            NatsAdapter.RecordStaticError(root, ex, "zbus_nats_jspub");
            ZWriter.WriteToNative((nint)dataZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Create or update a consumer on a stream. Starts consuming automatically.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_consumer &lt;0T1 &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc name) ← nats_consumer 'N1.ORDERS' 'proc' '' 'all'
    /// Arg 1: stream full name (e.g., 'N1.ORDERS')
    /// Arg 2: consumer name
    /// Arg 3: filter subject (empty = all)
    /// Input =Z: deliver policy ('all','last','new','last_per_subject')
    /// Output =Z: consumer full name
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_consumer")]
    public static int ZBusNatsConsumer(nint streamNamePtr, nint consumerNamePtr, nint filterPtr, nint* policyZ)
    {
        try
        {
            var streamFullName = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            var consumerName = Marshal.PtrToStringAnsi(consumerNamePtr) ?? "";
            var filterSubject = Marshal.PtrToStringAnsi(filterPtr) ?? "";
            if (string.IsNullOrEmpty(filterSubject)) filterSubject = null;

            // Read =Z input (deliver policy)
            nint payloadPtr = *policyZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            var deliverPolicy = value.AsString();

            // Extract root from stream full name
            var rootName = Bus.ExtractRootSegment(streamFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = adapter.CreateConsumerAsync(rootName, streamFullName, consumerName, filterSubject, deliverPolicy)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(fullName))
                {
                    ZWriter.WriteToNative((nint)policyZ, ZValue.EmptyChar);
                    return ReturnCodes.NotFound;
                }
                ZWriter.WriteToNative((nint)policyZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(streamNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_consumer");
            ZWriter.WriteToNative((nint)policyZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Acknowledge a JetStream message.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_ack &lt;0T1 I4'
    /// APL: rc ← nats_ack 'N1.ORDERS.proc' seqNo
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_ack")]
    public static int ZBusNatsAck(nint consumerNamePtr, int seq)
    {
        try
        {
            var consumerFullName = Marshal.PtrToStringAnsi(consumerNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(consumerFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.JsAckAsync(consumerFullName, seq).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(consumerNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_ack");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Negative-acknowledge a JetStream message (request redelivery).
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_nak &lt;0T1 I4'
    /// APL: rc ← nats_nak 'N1.ORDERS.proc' seqNo
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_nak")]
    public static int ZBusNatsNak(nint consumerNamePtr, int seq)
    {
        try
        {
            var consumerFullName = Marshal.PtrToStringAnsi(consumerNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(consumerFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.JsNakAsync(consumerFullName, seq).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(consumerNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_nak");
            return ReturnCodes.InternalError;
        }
    }

    private static byte[] ExtractPayloadBytes(ZValue value)
    {
        if (value.Type == ZType.Char)
            return System.Text.Encoding.UTF8.GetBytes(value.AsString());
        return value.AsBytes();
    }
}
