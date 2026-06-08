using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ZBus;
using ZBus.Adapters.Nats;
using ZFormat;

/// <summary>
/// NATS adapter verb exports and module initialization.
/// </summary>
public static unsafe class NatsExports
{
    [ModuleInitializer]
    internal static void Init()
    {
        Bus.RegisterAdapterFactory(() => new NatsAdapter());
    }

    /// <summary>
    /// Initiate a non-blocking connection to a NATS server.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_connect &lt;0T1 &lt;0T1'
    /// APL: rc ← nats_connect 'N1' 'nats://localhost:4222'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_connect")]
    public static int ZBusNatsConnect(nint rootNamePtr, nint urlPtr)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var url = Marshal.PtrToStringAnsi(urlPtr) ?? "";

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.Connect(rootName, url);
            });
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Publish a message to a subject.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_pub &lt;0T1 &lt;0T1 &lt;Z'
    /// APL: rc ← nats_pub 'N1' 'subject' 'hello'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_pub")]
    public static int ZBusNatsPub(nint rootNamePtr, nint subjectPtr, nint* dataZ)
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

            // Extract raw payload bytes
            byte[] payload;
            if (value.Type == ZType.Nested && value.Children.Length == 2)
            {
                // Shape-driven: (payload, headers) — headers ignored for now
                payload = ExtractPayloadBytes(value[0]);
            }
            else
            {
                payload = ExtractPayloadBytes(value);
            }

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                adapter.PublishAsync(subject, payload).GetAwaiter().GetResult();
                return ReturnCodes.OK;
            });
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Subscribe to a subject. Creates a subscription object.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_sub &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc name) ← nats_sub 'N1' 'prices' 'test.prices'
    /// APL: (rc name) ← nats_sub 'N1' 'worker1' ('tasks.>' 'pool')
    /// Input =Z: subject (char vector) or (subject, queueGroup) nested.
    /// Output =Z: the full dotted name of the created subscription object.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_sub")]
    public static int ZBusNatsSub(nint rootNamePtr, nint leafNamePtr, nint* subjectZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var leafName = Marshal.PtrToStringAnsi(leafNamePtr) ?? "";

            // Read =Z input (subject and optional queue group)
            nint payloadPtr = *subjectZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            string subject;
            string? queueGroup = null;

            if (value.Type == ZType.Nested && value.Children.Length == 2)
            {
                subject = value[0].AsString();
                queueGroup = value[1].AsString();
            }
            else
            {
                subject = value.AsString();
            }

            // Done reading input — now we can reuse the =Z slot for output
            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = string.IsNullOrEmpty(leafName)
                    ? $"{rootName}.sub_{Guid.NewGuid():N}"[..20]
                    : $"{rootName}.{leafName}";

                adapter.Subscribe(rootName, leafName, subject, queueGroup);
                ZWriter.WriteToNative((nint)subjectZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch
        {
            ZWriter.WriteToNative((nint)subjectZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    private static byte[] ExtractPayloadBytes(ZValue value)
    {
        if (value.Type == ZType.Char)
        {
            return System.Text.Encoding.UTF8.GetBytes(value.AsString());
        }
        // APLSINT or APLBOOL → byte[]
        return value.AsBytes();
    }
}
