using System.Runtime.InteropServices;
using ZBus;
using ZBus.Adapters.Nats;
using ZFormat;

/// <summary>
/// Key/Value Store ⎕NA verb exports.
/// </summary>
public static unsafe class KvExports
{
    /// <summary>
    /// Create or open a KV bucket.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_kv &lt;0T1 =Z'
    /// APL: (rc name) ← nats_kv 'N1' 'config'
    /// Input =Z: bucket name (char vector).
    /// Output =Z: full object name of the bucket.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_kv")]
    public static int ZBusNatsKv(nint rootNamePtr, nint* bucketZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";

            // Read =Z input (bucket name)
            nint payloadPtr = *bucketZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            var bucketName = value.AsString();

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = adapter.KvCreateBucketAsync(rootName, bucketName)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(fullName))
                {
                    ZWriter.WriteToNative((nint)bucketZ, ZValue.EmptyChar);
                    return ReturnCodes.InvalidHandle;
                }
                ZWriter.WriteToNative((nint)bucketZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            NatsAdapter.RecordStaticError(root, ex, "zbus_nats_kv");
            ZWriter.WriteToNative((nint)bucketZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Get a value from KV.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_kv_get &lt;0T1 &lt;0T1 >Z'
    /// APL: (rc val) ← nats_kv_get 'N1.config' 'api.timeout' 0
    /// Output >Z: nested (value, revision).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_kv_get")]
    public static int ZBusNatsKvGet(nint bucketNamePtr, nint keyPtr, nint* outZ)
    {
        try
        {
            var bucketFullName = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            var key = Marshal.PtrToStringAnsi(keyPtr) ?? "";
            var rootName = Bus.ExtractRootSegment(bucketFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var (rc, value, revision) = adapter.KvGetAsync(bucketFullName, key)
                    .GetAwaiter().GetResult();
                if (rc != ReturnCodes.OK || value == null)
                {
                    ZWriter.WriteToNative((nint)outZ, ZValue.EmptyNumeric);
                    return rc;
                }
                var result = ZValue.Nested(
                    ZValue.FromBytes(value),
                    ZValue.FromInt(revision)
                );
                ZWriter.WriteToNative((nint)outZ, result);
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_kv_get");
            ZWriter.WriteToNative((nint)outZ, ZValue.EmptyNumeric);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Put a value into KV.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_kv_put &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc rev) ← nats_kv_put 'N1.config' 'api.timeout' '30'
    /// Input =Z: payload (char or byte vector).
    /// Output =Z: revision number (scalar int).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_kv_put")]
    public static int ZBusNatsKvPut(nint bucketNamePtr, nint keyPtr, nint* dataZ)
    {
        try
        {
            var bucketFullName = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            var key = Marshal.PtrToStringAnsi(keyPtr) ?? "";
            var rootName = Bus.ExtractRootSegment(bucketFullName);

            // Read =Z input
            nint payloadPtr = *dataZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            var payload = ExtractPayloadBytes(value);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var (rc, revision) = adapter.KvPutAsync(bucketFullName, key, payload)
                    .GetAwaiter().GetResult();
                if (rc != ReturnCodes.OK)
                {
                    ZWriter.WriteToNative((nint)dataZ, ZValue.EmptyNumeric);
                    return rc;
                }
                ZWriter.WriteToNative((nint)dataZ, ZValue.FromInt(revision));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_kv_put");
            ZWriter.WriteToNative((nint)dataZ, ZValue.EmptyNumeric);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Delete a key from KV.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_kv_del &lt;0T1 &lt;0T1'
    /// APL: rc ← nats_kv_del 'N1.config' 'api.timeout'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_kv_del")]
    public static int ZBusNatsKvDel(nint bucketNamePtr, nint keyPtr)
    {
        try
        {
            var bucketFullName = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            var key = Marshal.PtrToStringAnsi(keyPtr) ?? "";
            var rootName = Bus.ExtractRootSegment(bucketFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.KvDeleteAsync(bucketFullName, key).GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_kv_del");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Watch a key pattern in KV. Posts KeyVal events via Wait.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_kv_watch &lt;0T1 &lt;0T1'
    /// APL: rc ← nats_kv_watch 'N1.config' 'api.>'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_kv_watch")]
    public static int ZBusNatsKvWatch(nint bucketNamePtr, nint keyPatternPtr)
    {
        try
        {
            var bucketFullName = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            var keyPattern = Marshal.PtrToStringAnsi(keyPatternPtr) ?? "";
            var rootName = Bus.ExtractRootSegment(bucketFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var watchName = adapter.KvWatch(bucketFullName, keyPattern);
                return string.IsNullOrEmpty(watchName) ? ReturnCodes.NotFound : ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(bucketNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_kv_watch");
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
