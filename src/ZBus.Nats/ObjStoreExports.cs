using System.Runtime.InteropServices;
using ZBus;
using ZBus.Adapters.Nats;
using ZFormat;

/// <summary>
/// Object Store ⎕NA verb exports.
/// </summary>
public static unsafe class ObjStoreExports
{
    /// <summary>
    /// Create or open an object store.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_obj &lt;0T1 =Z'
    /// APL: (rc name) ← nats_obj 'N1' 'files'
    /// Input =Z: store name (char vector).
    /// Output =Z: full object name of the store.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_obj")]
    public static int ZBusNatsObj(nint rootNamePtr, nint* storeZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";

            nint payloadPtr = *storeZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            var storeName = value.AsString();

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = adapter.ObjCreateStoreAsync(rootName, storeName)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(fullName))
                {
                    ZWriter.WriteToNative((nint)storeZ, ZValue.EmptyChar);
                    return ReturnCodes.InvalidHandle;
                }
                ZWriter.WriteToNative((nint)storeZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            NatsAdapter.RecordStaticError(root, ex, "zbus_nats_obj");
            ZWriter.WriteToNative((nint)storeZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Get an object from the store.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_obj_get &lt;0T1 &lt;0T1 >Z'
    /// APL: (rc data) ← nats_obj_get 'N1.files' 'report.pdf' 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_obj_get")]
    public static int ZBusNatsObjGet(nint storeNamePtr, nint objNamePtr, nint* outZ)
    {
        try
        {
            var storeFullName = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            var objName = Marshal.PtrToStringAnsi(objNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(storeFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var (rc, data) = adapter.ObjGetAsync(storeFullName, objName)
                    .GetAwaiter().GetResult();
                if (rc != ReturnCodes.OK || data == null)
                {
                    ZWriter.WriteToNative((nint)outZ, ZValue.EmptyNumeric);
                    return rc;
                }
                ZWriter.WriteToNative((nint)outZ, ZValue.FromBytes(data));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_obj_get");
            ZWriter.WriteToNative((nint)outZ, ZValue.EmptyNumeric);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Put an object into the store.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_obj_put &lt;0T1 &lt;0T1 &lt;Z'
    /// APL: rc ← nats_obj_put 'N1.files' 'report.pdf' payload
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_obj_put")]
    public static int ZBusNatsObjPut(nint storeNamePtr, nint objNamePtr, nint* dataZ)
    {
        try
        {
            var storeFullName = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            var objName = Marshal.PtrToStringAnsi(objNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(storeFullName);

            nint payloadPtr = *dataZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            var payload = ExtractPayloadBytes(value);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.ObjPutAsync(storeFullName, objName, payload)
                    .GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_obj_put");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Delete an object from the store.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_obj_del &lt;0T1 &lt;0T1'
    /// APL: rc ← nats_obj_del 'N1.files' 'report.pdf'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_obj_del")]
    public static int ZBusNatsObjDel(nint storeNamePtr, nint objNamePtr)
    {
        try
        {
            var storeFullName = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            var objName = Marshal.PtrToStringAnsi(objNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(storeFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.ObjDeleteAsync(storeFullName, objName)
                    .GetAwaiter().GetResult();
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_obj_del");
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Watch an object store for changes.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_obj_watch &lt;0T1 >Z'
    /// APL: (rc watchName) ← nats_obj_watch 'N1.files' 0
    /// Posts ObjChanged events with (name size operation) data.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_obj_watch")]
    public static int ZBusNatsObjWatch(nint storeNamePtr, nint* outZ)
    {
        try
        {
            var storeFullName = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(storeFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var watchName = adapter.ObjWatch(storeFullName);
                if (string.IsNullOrEmpty(watchName))
                {
                    ZWriter.WriteToNative((nint)outZ, ZValue.EmptyChar);
                    return ReturnCodes.NotFound;
                }
                ZWriter.WriteToNative((nint)outZ, ZValue.FromChars(watchName));
                return ReturnCodes.OK;
            });
        }
        catch (Exception ex)
        {
            var root = Marshal.PtrToStringAnsi(storeNamePtr) ?? "";
            NatsAdapter.RecordStaticError(Bus.ExtractRootSegment(root), ex, "zbus_nats_obj_watch");
            ZWriter.WriteToNative((nint)outZ, ZValue.EmptyChar);
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
