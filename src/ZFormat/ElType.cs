namespace ZFormat;

/// <summary>APL element types (from Dyalog's eltypes.h).</summary>
public enum ElType : byte
{
    APLNCHAR = 0,    // Classic narrow character (pre-Unicode)
    APLBOOL = 1,     // Boolean (bit-packed, MSB-first)
    APLSINT = 2,     // Signed 8-bit integer
    APLINTG = 3,     // Signed 16-bit integer
    APLLONG = 4,     // Signed 32-bit integer
    APLDOUB = 5,     // IEEE 754 64-bit double
    APLPNTR = 6,     // Nested element (pointer slot)
    APLWCHAR8 = 7,   // Unicode char squeezed to 8 bits (codepoints 0–255)
    APLWCHAR16 = 8,  // Unicode char squeezed to 16 bits
    APLWCHAR32 = 9,  // Full Unicode char (32 bits)
    APLCMPX = 10,    // Complex (2 × double)
    APLRATS = 11,    // Rational
    APLDECF_DPD = 12,// Decimal float (DPD)
    APLQUAD = 13,    // 64-bit integer
    APLDECF_BID = 14,// Decimal float (BID)
}

/// <summary>Pocket types relevant to Z format.</summary>
public enum PocketType : byte
{
    TYPEGEN = 7,     // Nested array
    TYPESIMPLE = 15, // Simple homogeneous array
}

/// <summary>Bytes per element for each type. Returns 0 for bit-packed APLBOOL.</summary>
public static class ElTypeInfo
{
    public static int BytesPerElement(ElType eltype) => eltype switch
    {
        ElType.APLNCHAR => 1,
        ElType.APLBOOL => 0, // bit-packed
        ElType.APLSINT => 1,
        ElType.APLINTG => 2,
        ElType.APLLONG => 4,
        ElType.APLDOUB => 8,
        ElType.APLPNTR => 8,
        ElType.APLWCHAR8 => 1,
        ElType.APLWCHAR16 => 2,
        ElType.APLWCHAR32 => 4,
        ElType.APLCMPX => 16,
        ElType.APLRATS => 16,
        ElType.APLDECF_DPD => 16,
        ElType.APLQUAD => 8,
        ElType.APLDECF_BID => 16,
        _ => 0,
    };

    public static bool IsChar(ElType eltype) =>
        eltype is ElType.APLNCHAR or ElType.APLWCHAR8 or ElType.APLWCHAR16 or ElType.APLWCHAR32;

    public static bool IsNumeric(ElType eltype) =>
        eltype is ElType.APLBOOL or ElType.APLSINT or ElType.APLINTG or ElType.APLLONG
            or ElType.APLDOUB or ElType.APLCMPX or ElType.APLQUAD
            or ElType.APLDECF_DPD or ElType.APLDECF_BID;

    public static bool IsInteger(ElType eltype) =>
        eltype is ElType.APLBOOL or ElType.APLSINT or ElType.APLINTG or ElType.APLLONG or ElType.APLQUAD;
}
