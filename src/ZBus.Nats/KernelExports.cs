using System.Runtime.InteropServices;
using ZBus;
using ZFormat;

/// <summary>
/// Kernel ⎕NA exports for ZBus. Present in every ZBus AOT DLL.
/// All exports return I4 (rc). Outputs use >Z via ZWriter.WriteToNative.
/// </summary>
public static unsafe class KernelExports
{
    /// <summary>
    /// Create or get a root by name.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_init &lt;0T1 >Z'
    /// APL: (rc rootName) ← init 'N1' 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_init")]
    public static int ZBusInit(nint namePtr, nint* outNameZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var (rc, root) = Bus.Init(name);
            if (rc != ReturnCodes.OK)
            {
                WriteEmpty(outNameZ);
                return rc;
            }
            ZWriter.WriteToNative((nint)outNameZ, ZValue.FromChars(root.Name));
            return ReturnCodes.OK;
        }
        catch
        {
            WriteEmpty(outNameZ);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Wait for an event. Blocks (designed for ⎕NA &amp;).
    /// ⎕NA: 'I4 ZBus.Nats|zbus_wait&amp; &lt;0T1 I4 >Z >Z >Z'
    /// APL: (rc obj evt data) ← wait 'N1' 5000 0 0 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_wait")]
    public static int ZBusWait(nint namePtr, int timeoutMs, nint* outObjZ, nint* outEvtZ, nint* outDataZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var root = Bus.FindRoot(name);
            if (root == null)
            {
                WriteEmpty(outObjZ);
                WriteEmpty(outEvtZ);
                WriteEmpty(outDataZ);
                return ReturnCodes.NotFound;
            }

            var evt = root.Wait(name, timeoutMs);
            if (evt == null)
            {
                ZWriter.WriteToNative((nint)outObjZ, ZValue.FromChars(name));
                ZWriter.WriteToNative((nint)outEvtZ, ZValue.FromChars("Timeout"));
                ZWriter.WriteToNative((nint)outDataZ, ZValue.EmptyNumeric);
                return ReturnCodes.OK;
            }

            ZWriter.WriteToNative((nint)outObjZ, ZValue.FromChars(evt.ObjectName));
            ZWriter.WriteToNative((nint)outEvtZ, ZValue.FromChars(evt.EventType));
            ZWriter.WriteToNative((nint)outDataZ, evt.Data);
            return ReturnCodes.OK;
        }
        catch
        {
            WriteEmpty(outObjZ);
            WriteEmpty(outEvtZ);
            WriteEmpty(outDataZ);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Close an object (or root). Cascading.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_close &lt;0T1'
    /// APL: rc ← close ⊂'N1.prices'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_close")]
    public static int ZBusClose(nint namePtr)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var rootName = Bus.ExtractRootSegment(name);

            if (name.Equals(rootName, StringComparison.OrdinalIgnoreCase))
            {
                return Bus.RemoveRoot(rootName) ? ReturnCodes.OK : ReturnCodes.NotFound;
            }

            var root = Bus.FindRoot(name);
            if (root == null) return ReturnCodes.NotFound;
            if (!root.Registry.Exists(name)) return ReturnCodes.NotFound;

            root.Close(name);
            return ReturnCodes.OK;
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// List children of a name.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_names &lt;0T1 >Z'
    /// APL: (rc children) ← names 'N1' 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_names")]
    public static int ZBusNames(nint namePtr, nint* outZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var root = Bus.FindRoot(name);
            if (root == null)
            {
                WriteEmpty(outZ);
                return ReturnCodes.NotFound;
            }

            var children = root.Registry.Children(name);
            ZWriter.WriteToNative((nint)outZ, ZValue.FromStringArray(children));
            return ReturnCodes.OK;
        }
        catch
        {
            WriteEmpty(outZ);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Check if a name exists.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_exists &lt;0T1'
    /// APL: rc ← exists ⊂'N1.prices'  (0=exists, NotFound=doesn't)
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_exists")]
    public static int ZBusExists(nint namePtr)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var root = Bus.FindRoot(name);
            if (root == null) return ReturnCodes.NotFound;
            return root.Registry.Exists(name) ? ReturnCodes.OK : ReturnCodes.NotFound;
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Get a property value.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_getprop &lt;0T1 &lt;0T1 >Z'
    /// APL: (rc value) ← getprop 'N1' 'State' 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_getprop")]
    public static int ZBusGetProp(nint namePtr, nint propPtr, nint* outZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var prop = Marshal.PtrToStringAnsi(propPtr) ?? "";
            var root = Bus.FindRoot(name);
            if (root == null)
            {
                WriteEmpty(outZ);
                return ReturnCodes.NotFound;
            }

            var value = root.GetProperty(name, prop);
            if (value == null)
            {
                WriteEmpty(outZ);
                return ReturnCodes.NotFound;
            }

            ZWriter.WriteToNative((nint)outZ, value);
            return ReturnCodes.OK;
        }
        catch
        {
            WriteEmpty(outZ);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Set a property value.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_setprop &lt;0T1 &lt;0T1 =Z'
    /// APL: rc ← setprop 'N1' 'Url' 'nats://localhost:4222'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_setprop")]
    public static int ZBusSetProp(nint namePtr, nint propPtr, nint* valueZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var prop = Marshal.PtrToStringAnsi(propPtr) ?? "";

            nint payloadPtr = *valueZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            var root = Bus.FindRoot(name);
            if (root == null) return ReturnCodes.NotFound;

            var ok = root.SetProperty(name, prop, value);
            return ok ? ReturnCodes.OK : ReturnCodes.NotFound;
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Describe an object (type, state, adapter metadata).
    /// ⎕NA: 'I4 ZBus.Nats|zbus_describe &lt;0T1 >Z'
    /// APL: (rc info) ← describe 'N1.sub1' 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_describe")]
    public static int ZBusDescribe(nint namePtr, nint* outZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            var root = Bus.FindRoot(name);
            if (root == null)
            {
                WriteEmpty(outZ);
                return ReturnCodes.NotFound;
            }

            var desc = root.Describe(name);
            if (desc == null)
            {
                WriteEmpty(outZ);
                return ReturnCodes.NotFound;
            }

            ZWriter.WriteToNative((nint)outZ, desc);
            return ReturnCodes.OK;
        }
        catch
        {
            WriteEmpty(outZ);
            return ReturnCodes.InternalError;
        }
    }

    private static void WriteEmpty(nint* outZ)
    {
        ZWriter.WriteToNative((nint)outZ, ZValue.EmptyChar);
    }
}
