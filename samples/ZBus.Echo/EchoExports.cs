using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using ZBus;
using ZBus.Adapters.Echo;
using ZFormat;

/// <summary>
/// Echo adapter verb exports and module initialization.
/// </summary>
public static unsafe class EchoExports
{
    [ModuleInitializer]
    internal static void Init()
    {
        Bus.RegisterAdapterFactory(() => new EchoAdapter());
    }

    /// <summary>
    /// Create a timer that posts Tick events.
    /// ⎕NA: 'I4 ZBus.Echo|zbus_echo_timer &lt;0T1 &lt;0T1 I4 >Z'
    /// APL: (rc fullName) ← timer 'MyBus' 'T1' 1000 0
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_echo_timer")]
    public static int ZBusEchoTimer(nint parentNamePtr, nint leafNamePtr, int intervalMs, nint* outNameZ)
    {
        try
        {
            var parentName = Marshal.PtrToStringAnsi(parentNamePtr) ?? "";
            var leafName = Marshal.PtrToStringAnsi(leafNamePtr) ?? "";

            return Dispatch.Execute<EchoAdapter>(parentName, (adapter, root) =>
            {
                var fullName = adapter.CreateTimer(parentName, leafName, intervalMs);
                ZWriter.WriteToNative((nint)outNameZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch
        {
            ZWriter.WriteToNative((nint)outNameZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Echo: send data to an object; it comes back as a Receive event.
    /// ⎕NA: 'I4 ZBus.Echo|zbus_echo_send &lt;0T1 =Z'
    /// APL: rc ← send 'MyBus.T1' (⊂'hello')
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_echo_send")]
    public static int ZBusEchoSend(nint namePtr, nint* dataZ)
    {
        try
        {
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";

            // Read =Z input
            nint payloadPtr = *dataZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            return Dispatch.Execute<EchoAdapter>(name, (adapter, root) =>
            {
                adapter.Send(name, value);
                return ReturnCodes.OK;
            });
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }
}
