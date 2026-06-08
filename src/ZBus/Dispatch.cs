using ZFormat;

namespace ZBus;

/// <summary>
/// Dispatch helper for adapter verb exports.
/// Handles the boilerplate: string reading, root lookup, adapter resolution,
/// and error wrapping (try/catch → rc).
/// </summary>
public static class Dispatch
{
    /// <summary>
    /// Look up the root from a dotted name, resolve the adapter, and invoke the action.
    /// Returns the appropriate return code on failure.
    /// </summary>
    public static int Execute<TAdapter>(string dottedName, Func<TAdapter, Root, int> action)
        where TAdapter : class, IAdapter
    {
        try
        {
            var root = Bus.FindRoot(dottedName);
            if (root == null)
                return ReturnCodes.NotFound;

            var adapter = root.GetAdapter<TAdapter>();
            if (adapter == null)
                return ReturnCodes.InternalError;

            return action(adapter, root);
        }
        catch
        {
            return ReturnCodes.InternalError;
        }
    }
}
