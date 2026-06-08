namespace ZBus;

/// <summary>
/// Kernel return codes. These are the only values that appear as the
/// int return from ⎕NA exports. Adapter-specific errors use AdapterError
/// and put details in the event data.
/// </summary>
public static class ReturnCodes
{
    public const int OK = 0;
    public const int InvalidHandle = 1;
    public const int InvalidName = 2;
    public const int NotFound = 3;
    public const int NameInUse = 4;
    public const int AdapterError = 5;
    public const int QueueOverflow = 6;
    public const int InvalidArgument = 7;
    public const int InternalError = 8;
}
