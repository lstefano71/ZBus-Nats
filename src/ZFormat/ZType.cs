namespace ZFormat;

/// <summary>
/// Coarse element-type family for a Z value.
/// Rank is orthogonal — always check <see cref="ZValue.Shape"/> for dimensionality.
/// </summary>
public enum ZType
{
    Bool,
    Byte,
    Char,
    Int,
    Double,
    Decf,
    Nested,
}
