namespace ZFormat;

/// <summary>
/// Encodes and decodes the 64-bit zones bitfield used in pocket headers.
/// </summary>
public readonly record struct Zones(long Value)
{
    public PocketType Type => (PocketType)(Value & 0xF);
    public int Rank => (int)((Value >> 4) & 0xF);
    public ElType ElType => (ElType)((Value >> 8) & 0xF);
    public bool Sticky => ((Value >> 12) & 1) != 0;
    public bool Squoze => ((Value >> 13) & 1) != 0;

    public bool IsSimple => Type == PocketType.TYPESIMPLE;
    public bool IsNested => Type == PocketType.TYPEGEN;

    public static Zones Make(PocketType type, int rank, ElType eltype, bool squoze = true)
    {
        long v = ((int)type & 0xF)
               | ((rank & 0xF) << 4)
               | (((int)eltype & 0xF) << 8);
        if (squoze) v |= 1L << 13;
        return new Zones(v);
    }

    public static Zones Simple(int rank, ElType eltype) =>
        Make(PocketType.TYPESIMPLE, rank, eltype, squoze: true);

    public static Zones Nested(int rank) =>
        Make(PocketType.TYPEGEN, rank, ElType.APLPNTR, squoze: false);

    public override string ToString() =>
        $"0x{Value:X4} ({Type}|rank{Rank}|{ElType}{(Squoze ? "|squoze" : "")})";
}
