using System.Runtime.InteropServices;
using ZBus;
using ZBus.Adapters.Nats;
using ZFormat;

/// <summary>
/// NATS Services ⎕NA verb exports.
/// </summary>
public static unsafe class ServiceExports
{
    /// <summary>
    /// Register a NATS micro-service.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_service &lt;0T1 &lt;0T1 =Z'
    /// APL: (rc name) ← nats_service 'N1' 'calc' ('Math service' '1.0')
    /// Input =Z: (description, version) nested pair.
    /// Output =Z: service full object name.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_service")]
    public static int ZBusNatsService(nint rootNamePtr, nint serviceNamePtr, nint* configZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var serviceName = Marshal.PtrToStringAnsi(serviceNamePtr) ?? "";

            // Read =Z input (description, version)
            nint payloadPtr = *configZ;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);

            string description = "";
            string version = "1.0.0";
            if (value.Type == ZType.Nested && value.Children.Length >= 2)
            {
                description = value[0].AsString();
                version = value[1].AsString();
            }
            else if (value.Type == ZType.Char)
            {
                version = value.AsString();
            }

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var fullName = adapter.CreateServiceAsync(rootName, serviceName, description, version)
                    .GetAwaiter().GetResult();
                if (string.IsNullOrEmpty(fullName))
                {
                    ZWriter.WriteToNative((nint)configZ, ZValue.EmptyChar);
                    return ReturnCodes.InvalidHandle;
                }
                ZWriter.WriteToNative((nint)configZ, ZValue.FromChars(fullName));
                return ReturnCodes.OK;
            });
        }
        catch
        {
            ZWriter.WriteToNative((nint)configZ, ZValue.EmptyChar);
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Add an endpoint to a service. Incoming requests arrive as Request events.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_endpoint &lt;0T1 &lt;0T1 &lt;0T1'
    /// APL: rc ← nats_endpoint 'N1.calc' 'add' 'math.add'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_endpoint")]
    public static int ZBusNatsEndpoint(nint serviceNamePtr, nint endpointNamePtr, nint subjectPtr)
    {
        try
        {
            var serviceFullName = Marshal.PtrToStringAnsi(serviceNamePtr) ?? "";
            var endpointName = Marshal.PtrToStringAnsi(endpointNamePtr) ?? "";
            var subject = Marshal.PtrToStringAnsi(subjectPtr) ?? "";
            var rootName = Bus.ExtractRootSegment(serviceFullName);

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                return adapter.AddEndpointAsync(serviceFullName, endpointName, subject)
                    .GetAwaiter().GetResult();
            });
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }

    /// <summary>
    /// Discover available NATS micro-services.
    /// ⎕NA: 'I4 ZBus.Nats|zbus_nats_svc_discover &lt;0T1 &lt;0T1 I4 >Z'
    /// APL: (rc services) ← nats_svc_discover 'N1' '' 1000 0
    /// serviceName='' discovers all; timeout in ms.
    /// Output >Z: nested array of (name id version) per service.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "zbus_nats_svc_discover")]
    public static int ZBusNatsSvcDiscover(nint rootNamePtr, nint serviceNamePtr, int timeoutMs, nint* outZ)
    {
        try
        {
            var rootName = Marshal.PtrToStringAnsi(rootNamePtr) ?? "";
            var serviceNameFilter = Marshal.PtrToStringAnsi(serviceNamePtr) ?? "";

            return Dispatch.Execute<NatsAdapter>(rootName, (adapter, root) =>
            {
                var result = adapter.DiscoverServicesAsync(serviceNameFilter, timeoutMs)
                    .GetAwaiter().GetResult();
                ZWriter.WriteToNative((nint)outZ, result);
                return ReturnCodes.OK;
            });
        }
        catch
        {
            ZWriter.WriteToNative((nint)outZ, ZValue.EmptyNumeric);
            return ReturnCodes.InternalError;
        }
    }
}
