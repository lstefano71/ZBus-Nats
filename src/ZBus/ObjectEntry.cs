namespace ZBus;

/// <summary>
/// Metadata about a registered object in the hierarchy.
/// </summary>
public sealed class ObjectEntry
{
    public required string FullName { get; init; }
    public required string LeafName { get; init; }
    public required string ParentFullName { get; init; }
    public required string ObjectType { get; init; }
    public required string AdapterId { get; init; }
}
