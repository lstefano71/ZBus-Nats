using System.Runtime.InteropServices;
using System.Text;

namespace ZFormat;

/// <summary>
/// Represents a decoded APL value from a Z buffer.
/// Discriminated by <see cref="Type"/>. Use the typed accessors to extract data.
/// </summary>
public sealed class ZValue
{
    /// <summary>Coarse element-type family. Rank is orthogonal — check <see cref="Shape"/>.</summary>
    public ZType Type { get; }

    /// <summary>Exact storage element type (for power users who need wire-level detail).</summary>
    public ElType ElType => Zones.ElType;

    /// <summary>Array shape. Length == 0 means scalar; Length == 1 means vector; etc.</summary>
    public long[] Shape { get; }

    internal Zones Zones { get; }

    // Data storage — exactly one is populated depending on Type
    private readonly byte[]? _bytes;
    private readonly ZValue[]? _children;
    private readonly ZValue? _prototype;

    private ZValue(ZType type, Zones zones, long[] shape, byte[]? bytes = null, ZValue[]? children = null, ZValue? prototype = null)
    {
        Type = type;
        Zones = zones;
        Shape = shape;
        _bytes = bytes;
        _children = children;
        _prototype = prototype;
    }

    /// <summary>Total element count (product of shape dimensions).</summary>
    public long ElementCount
    {
        get
        {
            if (Shape.Length == 0) return 1; // scalar
            long count = 1;
            foreach (var dim in Shape) count *= dim;
            return count;
        }
    }

    /// <summary>Raw data bytes (for simple arrays). Internal — use typed accessors.</summary>
    internal ReadOnlySpan<byte> RawData => _bytes ?? [];

    /// <summary>Raw data bytes as array (internal, for ZWriter).</summary>
    internal byte[]? RawBytes => _bytes;

    /// <summary>Children (for nested arrays).</summary>
    public ReadOnlySpan<ZValue> Children => _children ?? [];

    /// <summary>
    /// Prototype for empty nested arrays. In APL, every empty nested array has a prototype
    /// that determines the structure of elements created by take (1↑) or fill.
    /// Null for non-empty arrays and simple arrays.
    /// </summary>
    public ZValue? Prototype => _prototype;

    /// <summary>Indexer for nested children. Equivalent to Children[index].</summary>
    public ZValue this[int index] => (_children ?? throw new InvalidOperationException("Not a nested array"))[index];

    // ─── Typed accessors ───

    /// <summary>Read as string (valid for APLWCHAR8/16/32 and APLNCHAR).</summary>
    public string AsString()
    {
        if (!ElTypeInfo.IsChar(Zones.ElType))
            throw new InvalidOperationException($"Not a character array (eltype={Zones.ElType})");

        int count = (int)ElementCount;
        if (count == 0) return "";

        return Zones.ElType switch
        {
            ElType.APLNCHAR or ElType.APLWCHAR8 => AsStringFromBytes(count),
            ElType.APLWCHAR16 => AsStringFromChar16(count),
            ElType.APLWCHAR32 => AsStringFromChar32(count),
            _ => throw new NotSupportedException()
        };
    }

    private string AsStringFromBytes(int count)
    {
        // Each byte is a raw Unicode codepoint (0–255)
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)_bytes![i];
        return new string(chars);
    }

    private string AsStringFromChar16(int count)
    {
        var span = MemoryMarshal.Cast<byte, ushort>(_bytes.AsSpan());
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)span[i];
        return new string(chars);
    }

    private string AsStringFromChar32(int count)
    {
        var span = MemoryMarshal.Cast<byte, int>(_bytes.AsSpan());
        var builder = new StringBuilder(count * 2);
        for (int i = 0; i < count; i++)
            builder.Append(char.ConvertFromUtf32(span[i]));
        return builder.ToString();
    }

    /// <summary>Read as int array. Supports APLBOOL, APLSINT, APLINTG, APLLONG, APLQUAD.</summary>
    public long[] AsInt64Array()
    {
        int count = (int)ElementCount;
        var result = new long[count];

        switch (Zones.ElType)
        {
            case ElType.APLBOOL:
                for (int i = 0; i < count; i++)
                {
                    int byteIdx = i / 8;
                    int bitIdx = 7 - (i % 8); // MSB-first
                    result[i] = (_bytes![byteIdx] >> bitIdx) & 1;
                }
                break;
            case ElType.APLSINT:
                for (int i = 0; i < count; i++)
                    result[i] = (sbyte)_bytes![i];
                break;
            case ElType.APLINTG:
                var i16 = MemoryMarshal.Cast<byte, short>(_bytes.AsSpan());
                for (int i = 0; i < count; i++)
                    result[i] = i16[i];
                break;
            case ElType.APLLONG:
                var i32 = MemoryMarshal.Cast<byte, int>(_bytes.AsSpan());
                for (int i = 0; i < count; i++)
                    result[i] = i32[i];
                break;
            case ElType.APLQUAD:
                var i64 = MemoryMarshal.Cast<byte, long>(_bytes.AsSpan());
                for (int i = 0; i < count; i++)
                    result[i] = i64[i];
                break;
            default:
                throw new InvalidOperationException($"Not an integer type: {Zones.ElType}");
        }
        return result;
    }

    /// <summary>Read a scalar integer value.</summary>
    public long AsInt64()
    {
        if (Shape.Length != 0) throw new InvalidOperationException("Not a scalar");
        return AsInt64Array()[0];
    }

    /// <summary>Read as double array.</summary>
    public double[] AsDoubleArray()
    {
        if (Zones.ElType != ElType.APLDOUB)
            throw new InvalidOperationException($"Not a double array: {Zones.ElType}");
        int count = (int)ElementCount;
        var src = MemoryMarshal.Cast<byte, double>(_bytes.AsSpan());
        var result = new double[count];
        src[..count].CopyTo(result);
        return result;
    }

    /// <summary>Read a scalar double value.</summary>
    public double AsDouble()
    {
        if (Shape.Length != 0) throw new InvalidOperationException("Not a scalar");
        return AsDoubleArray()[0];
    }

    /// <summary>Read raw DECF (BID128) bytes. Returns 16 bytes per element.</summary>
    public byte[] AsDecfBytes()
    {
        if (Zones.ElType is not (ElType.APLDECF_BID or ElType.APLDECF_DPD))
            throw new InvalidOperationException($"Not a DECF type: {Zones.ElType}");
        return _bytes!.ToArray();
    }

    /// <summary>
    /// Read a DECF scalar as a 64-bit integer (valid only when the DECF represents an exact integer).
    /// Extracts coefficient from BID128 format assuming exponent=0.
    /// </summary>
    public long AsDecfInt64()
    {
        if (Zones.ElType is not (ElType.APLDECF_BID or ElType.APLDECF_DPD))
            throw new InvalidOperationException($"Not a DECF type: {Zones.ElType}");
        // BID128: low 8 bytes = coefficient (LE), high 8 bytes = combination field
        var low = MemoryMarshal.Cast<byte, long>(_bytes.AsSpan())[0];
        var high = MemoryMarshal.Cast<byte, long>(_bytes.AsSpan(8))[0];
        bool negative = (high & unchecked((long)0x8000000000000000UL)) != 0;
        return negative ? -low : low;
    }

    // ─── Zero-copy span accessors ───

    /// <summary>Zero-copy reinterpret as Int32 span. Only valid when ElType is APLLONG.</summary>
    public ReadOnlySpan<int> SpanInt32()
    {
        if (Zones.ElType != ElType.APLLONG)
            throw new InvalidOperationException($"SpanInt32 requires APLLONG, got {Zones.ElType}");
        return MemoryMarshal.Cast<byte, int>(_bytes.AsSpan());
    }

    /// <summary>Zero-copy reinterpret as Double span. Only valid when ElType is APLDOUB.</summary>
    public ReadOnlySpan<double> SpanDouble()
    {
        if (Zones.ElType != ElType.APLDOUB)
            throw new InvalidOperationException($"SpanDouble requires APLDOUB, got {Zones.ElType}");
        return MemoryMarshal.Cast<byte, double>(_bytes.AsSpan());
    }

    /// <summary>Zero-copy reinterpret as Int16 span. Only valid when ElType is APLINTG.</summary>
    public ReadOnlySpan<short> SpanInt16()
    {
        if (Zones.ElType != ElType.APLINTG)
            throw new InvalidOperationException($"SpanInt16 requires APLINTG, got {Zones.ElType}");
        return MemoryMarshal.Cast<byte, short>(_bytes.AsSpan());
    }

    /// <summary>Zero-copy reinterpret as Int64 span. Only valid when ElType is APLQUAD.</summary>
    public ReadOnlySpan<long> SpanInt64()
    {
        if (Zones.ElType != ElType.APLQUAD)
            throw new InvalidOperationException($"SpanInt64 requires APLQUAD, got {Zones.ElType}");
        return MemoryMarshal.Cast<byte, long>(_bytes.AsSpan());
    }

    // ─── Factories ───

    /// <summary>Create a simple array with arbitrary rank and shape.</summary>
    public static ZValue FromSimple(ElType elType, long[] shape, byte[] data)
    {
        var zones = Zones.Simple(shape.Length, elType);
        return new ZValue(ClassifyType(zones), zones, shape, data);
    }

    /// <summary>Create a boolean array with explicit shape (row-major, flat bit packing, no per-row padding).</summary>
    public static ZValue FromBools(long[] shape, ReadOnlySpan<bool> data)
    {
        long totalElements = 1;
        foreach (var dim in shape) totalElements *= dim;
        if (data.Length != totalElements)
            throw new ArgumentException($"Expected {totalElements} booleans for shape [{string.Join(",", shape)}]");

        var bytes = PackBools(data);
        return new ZValue(ZType.Bool, Zones.Simple(shape.Length, ElType.APLBOOL), shape, bytes);
    }

    /// <summary>Create a boolean vector.</summary>
    public static ZValue FromBools(ReadOnlySpan<bool> data)
    {
        var bytes = PackBools(data);
        return new ZValue(ZType.Bool, Zones.Simple(1, ElType.APLBOOL), [data.Length], bytes);
    }

    /// <summary>Create a boolean scalar.</summary>
    public static ZValue FromBool(bool v) =>
        new(ZType.Bool, Zones.Simple(0, ElType.APLBOOL), [], [v ? (byte)0x80 : (byte)0]);

    /// <summary>Create an Int32 array with explicit shape.</summary>
    public static ZValue FromInt32Array(long[] shape, ReadOnlySpan<int> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLLONG), shape, bytes);
    }

    /// <summary>Create a double array with explicit shape.</summary>
    public static ZValue FromDoubles(long[] shape, ReadOnlySpan<double> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZType.Double, Zones.Simple(shape.Length, ElType.APLDOUB), shape, bytes);
    }

    /// <summary>Create a character array with explicit shape.</summary>
    public static ZValue FromChars(long[] shape, string data)
    {
        long totalElements = 1;
        foreach (var dim in shape) totalElements *= dim;
        var runes = data.EnumerateRunes().ToArray();
        if (runes.Length != totalElements)
            throw new ArgumentException($"Expected {totalElements} characters for shape");

        var bytes = EncodeRunes(runes, out var eltype);
        return new ZValue(ZType.Char, Zones.Simple(shape.Length, eltype), shape, bytes);
    }

    /// <summary>Create a character vector from a string.</summary>
    public static ZValue FromChars(string s)
    {
        if (s.Length == 0)
            return new ZValue(ZType.Char, Zones.Simple(1, ElType.APLWCHAR8), [0], []);

        var runes = s.EnumerateRunes().ToArray();
        var bytes = EncodeRunes(runes, out var eltype);
        return new ZValue(ZType.Char, Zones.Simple(1, eltype), [runes.Length], bytes);
    }

    /// <summary>Create a character vector from a string.</summary>
    [Obsolete("Use FromChars instead.")]
    public static ZValue FromString(string s) => FromChars(s);

    /// <summary>Create a character array with explicit shape.</summary>
    [Obsolete("Use FromChars(long[], string) instead.")]
    public static ZValue FromCharArray(long[] shape, string data) => FromChars(shape, data);

    private static byte[] EncodeRunes(Rune[] runes, out ElType eltype)
    {
        int maxCodepoint = 0;
        foreach (var rune in runes)
            maxCodepoint = Math.Max(maxCodepoint, rune.Value);

        eltype = maxCodepoint <= 255 ? ElType.APLWCHAR8 : (maxCodepoint <= 65535 ? ElType.APLWCHAR16 : ElType.APLWCHAR32);

        switch (eltype)
        {
            case ElType.APLWCHAR8:
                var bytes8 = new byte[runes.Length];
                for (int i = 0; i < runes.Length; i++) bytes8[i] = (byte)runes[i].Value;
                return bytes8;
            case ElType.APLWCHAR16:
                var bytes16 = new byte[runes.Length * 2];
                var u16 = MemoryMarshal.Cast<byte, ushort>(bytes16.AsSpan());
                for (int i = 0; i < runes.Length; i++) u16[i] = (ushort)runes[i].Value;
                return bytes16;
            default:
                var bytes32 = new byte[runes.Length * 4];
                var u32 = MemoryMarshal.Cast<byte, int>(bytes32.AsSpan());
                for (int i = 0; i < runes.Length; i++) u32[i] = runes[i].Value;
                return bytes32;
        }
    }

    /// <summary>Create a scalar character.</summary>
    public static ZValue FromChar(char c)
    {
        ElType eltype = c <= 255 ? ElType.APLWCHAR8 : (c <= 65535 ? ElType.APLWCHAR16 : ElType.APLWCHAR32);
        int size = ElTypeInfo.BytesPerElement(eltype);
        var bytes = new byte[size];
        switch (eltype)
        {
            case ElType.APLWCHAR8: bytes[0] = (byte)c; break;
            case ElType.APLWCHAR16: MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan())[0] = c; break;
            case ElType.APLWCHAR32: MemoryMarshal.Cast<byte, int>(bytes.AsSpan())[0] = c; break;
        }
        return new ZValue(ZType.Char, Zones.Simple(0, eltype), [], bytes);
    }

    /// <summary>Create a scalar character from a Unicode rune.</summary>
    public static ZValue FromRune(Rune r)
    {
        ElType eltype = r.Value <= 255 ? ElType.APLWCHAR8 : (r.Value <= 65535 ? ElType.APLWCHAR16 : ElType.APLWCHAR32);
        int size = ElTypeInfo.BytesPerElement(eltype);
        var bytes = new byte[Math.Max(size, 4)]; // ensure room for 32-bit write
        switch (eltype)
        {
            case ElType.APLWCHAR8: bytes[0] = (byte)r.Value; break;
            case ElType.APLWCHAR16: MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan())[0] = (ushort)r.Value; break;
            case ElType.APLWCHAR32: MemoryMarshal.Cast<byte, int>(bytes.AsSpan())[0] = r.Value; break;
        }
        return new ZValue(ZType.Char, Zones.Simple(0, eltype), [], bytes[..size]);
    }

    // ─── Explicit-width integer factories (escape hatches) ───

    /// <summary>Create a scalar 8-bit integer (APLSINT).</summary>
    public static ZValue FromInt8(sbyte v) =>
        new(ZType.Int, Zones.Simple(0, ElType.APLSINT), [], [(byte)v]);

    /// <summary>Create a scalar 16-bit integer (APLINTG).</summary>
    public static ZValue FromInt16(short v)
    {
        var bytes = new byte[2];
        MemoryMarshal.Cast<byte, short>(bytes.AsSpan())[0] = v;
        return new ZValue(ZType.Int, Zones.Simple(0, ElType.APLINTG), [], bytes);
    }

    /// <summary>Create a scalar 32-bit integer (APLLONG).</summary>
    public static ZValue FromInt32(int v)
    {
        var bytes = new byte[4];
        MemoryMarshal.Cast<byte, int>(bytes.AsSpan())[0] = v;
        return new ZValue(ZType.Int, Zones.Simple(0, ElType.APLLONG), [], bytes);
    }

    /// <summary>Create a scalar 64-bit integer (APLQUAD).</summary>
    public static ZValue FromInt64(long v)
    {
        var bytes = new byte[8];
        MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = v;
        return new ZValue(ZType.Int, Zones.Simple(0, ElType.APLQUAD), [], bytes);
    }

    /// <summary>Create an integer scalar, auto-squeezing to the smallest storage type.</summary>
    public static ZValue FromInt(long v) => v switch
    {
        >= 0 and <= 127 => FromInt8((sbyte)v),
        >= -128 and <= 127 => FromInt8((sbyte)v),
        >= short.MinValue and <= short.MaxValue => FromInt16((short)v),
        >= int.MinValue and <= int.MaxValue => FromInt32((int)v),
        _ => FromInt64(v),
    };

    /// <summary>Create an integer scalar, auto-squeezing to the smallest storage type.</summary>
    [Obsolete("Use FromInt instead.")]
    public static ZValue FromIntSqueezed(long v) => FromInt(v);

    /// <summary>Create an integer vector from Int32 data (APLLONG storage).</summary>
    public static ZValue FromInt32Array(ReadOnlySpan<int> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLLONG), [data.Length], bytes);
    }

    /// <summary>Create an integer vector from long data, auto-squeezing element width.</summary>
    public static ZValue FromInts(ReadOnlySpan<long> data)
    {
        if (data.Length == 0)
            return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLSINT), [0], []);

        // Determine smallest type that fits all values
        long min = long.MaxValue, max = long.MinValue;
        foreach (var v in data) { if (v < min) min = v; if (v > max) max = v; }

        if (min >= -128 && max <= 127)
        {
            var bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) bytes[i] = (byte)(sbyte)data[i];
            return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLSINT), [data.Length], bytes);
        }
        if (min >= short.MinValue && max <= short.MaxValue)
        {
            var bytes = new byte[data.Length * 2];
            var span = MemoryMarshal.Cast<byte, short>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = (short)data[i];
            return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLINTG), [data.Length], bytes);
        }
        if (min >= int.MinValue && max <= int.MaxValue)
        {
            var bytes = new byte[data.Length * 4];
            var span = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = (int)data[i];
            return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLLONG), [data.Length], bytes);
        }
        {
            var bytes = new byte[data.Length * 8];
            var span = MemoryMarshal.Cast<byte, long>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = data[i];
            return new ZValue(ZType.Int, Zones.Simple(1, ElType.APLQUAD), [data.Length], bytes);
        }
    }

    /// <summary>Create an integer array with explicit shape, auto-squeezing element width.</summary>
    public static ZValue FromInts(long[] shape, ReadOnlySpan<long> data)
    {
        if (data.Length == 0)
            return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLSINT), shape, []);

        long min = long.MaxValue, max = long.MinValue;
        foreach (var v in data) { if (v < min) min = v; if (v > max) max = v; }

        if (min >= -128 && max <= 127)
        {
            var bytes = new byte[data.Length];
            for (int i = 0; i < data.Length; i++) bytes[i] = (byte)(sbyte)data[i];
            return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLSINT), shape, bytes);
        }
        if (min >= short.MinValue && max <= short.MaxValue)
        {
            var bytes = new byte[data.Length * 2];
            var span = MemoryMarshal.Cast<byte, short>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = (short)data[i];
            return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLINTG), shape, bytes);
        }
        if (min >= int.MinValue && max <= int.MaxValue)
        {
            var bytes = new byte[data.Length * 4];
            var span = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = (int)data[i];
            return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLLONG), shape, bytes);
        }
        {
            var bytes = new byte[data.Length * 8];
            var span = MemoryMarshal.Cast<byte, long>(bytes.AsSpan());
            for (int i = 0; i < data.Length; i++) span[i] = data[i];
            return new ZValue(ZType.Int, Zones.Simple(shape.Length, ElType.APLQUAD), shape, bytes);
        }
    }

    /// <summary>Create a scalar double.</summary>
    public static ZValue FromDouble(double v)
    {
        var bytes = new byte[8];
        MemoryMarshal.Cast<byte, double>(bytes.AsSpan())[0] = v;
        return new ZValue(ZType.Double, Zones.Simple(0, ElType.APLDOUB), [], bytes);
    }

    /// <summary>Create a double vector.</summary>
    public static ZValue FromDoubles(ReadOnlySpan<double> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZType.Double, Zones.Simple(1, ElType.APLDOUB), [data.Length], bytes);
    }

    /// <summary>Create a double vector.</summary>
    [Obsolete("Use FromDoubles instead.")]
    public static ZValue FromDoubleArray(ReadOnlySpan<double> data) => FromDoubles(data);

    /// <summary>
    /// Create a DECF (decimal128 BID) scalar from raw 16-byte representation.
    /// </summary>
    public static ZValue FromDecf(ReadOnlySpan<byte> bid128Bytes)
    {
        if (bid128Bytes.Length != 16)
            throw new ArgumentException("DECF requires exactly 16 bytes (BID128 format)");
        return new ZValue(ZType.Decf, Zones.Simple(0, ElType.APLDECF_BID), [], bid128Bytes.ToArray());
    }

    /// <summary>
    /// Create a DECF scalar encoding a 64-bit integer exactly.
    /// </summary>
    public static ZValue FromDecfInt64(long v)
    {
        var bytes = new byte[16];
        if (v >= 0)
        {
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = v;
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan(8))[0] = 0x3040000000000000L;
        }
        else
        {
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = -v;
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan(8))[0] = unchecked((long)0xB040000000000000UL);
        }
        return new ZValue(ZType.Decf, Zones.Simple(0, ElType.APLDECF_BID), [], bytes);
    }

    /// <summary>Create a DECF vector from raw 16-byte elements.</summary>
    public static ZValue FromDecfs(ReadOnlySpan<byte> bid128Data, int elementCount)
    {
        if (bid128Data.Length != elementCount * 16)
            throw new ArgumentException($"Expected {elementCount * 16} bytes for {elementCount} DECF elements");
        return new ZValue(ZType.Decf, Zones.Simple(1, ElType.APLDECF_BID), [elementCount], bid128Data.ToArray());
    }

    /// <summary>Create a DECF vector from raw 16-byte elements.</summary>
    [Obsolete("Use FromDecfs instead.")]
    public static ZValue FromDecfArray(ReadOnlySpan<byte> bid128Data, int elementCount) => FromDecfs(bid128Data, elementCount);

    /// <summary>Create a byte vector (APLSINT storage).</summary>
    public static ZValue FromBytes(ReadOnlySpan<byte> data) =>
        new(ZType.Byte, Zones.Simple(1, ElType.APLSINT), [data.Length], data.ToArray());

    /// <summary>Create a nested vector.</summary>
    public static ZValue Nested(params ZValue[] items) =>
        new(ZType.Nested, Zones.Nested(1), [items.Length], children: items);

    /// <summary>Create a nested array with explicit rank and shape.</summary>
    public static ZValue Nested(long[] shape, ZValue[] items) =>
        new(ZType.Nested, Zones.Nested(shape.Length), shape, children: items);

    /// <summary>
    /// Create an empty nested array with a prototype.
    /// The prototype determines the structure of fill elements (used by dyadic ↑, etc.).
    /// </summary>
    /// <param name="shape">Shape with at least one zero dimension.</param>
    /// <param name="prototype">The prototype value (e.g., FromChars("") for char prototype).</param>
    public static ZValue EmptyNested(long[] shape, ZValue prototype) =>
        new(ZType.Nested, Zones.Nested(shape.Length), shape, children: [], prototype: prototype);

    /// <summary>
    /// Create an empty nested vector (shape=[0]) with a prototype.
    /// </summary>
    public static ZValue EmptyNested(ZValue prototype) =>
        EmptyNested([0], prototype);

    // ─── Obsolete aliases for backward compat ───

    /// <summary>Create a boolean vector.</summary>
    [Obsolete("Use FromBools instead.")]
    public static ZValue FromBooleans(ReadOnlySpan<bool> data) => FromBools(data);

    /// <summary>Create a boolean array with explicit shape.</summary>
    [Obsolete("Use FromBools(long[], ReadOnlySpan&lt;bool&gt;) instead.")]
    public static ZValue FromBooleans(long[] shape, ReadOnlySpan<bool> data) => FromBools(shape, data);

    /// <summary>Create a higher-rank double array.</summary>
    [Obsolete("Use FromDoubles(long[], ReadOnlySpan&lt;double&gt;) instead.")]
    public static ZValue FromDoubleArray(long[] shape, ReadOnlySpan<double> data) => FromDoubles(shape, data);

    // ─── Internal factory for ZReader ───

    internal static ZValue FromRaw(Zones zones, long[] shape, byte[] data) =>
        new(ClassifyType(zones), zones, shape, data);

    internal static ZValue FromNested(Zones zones, long[] shape, ZValue[] children, ZValue? prototype = null) =>
        new(ZType.Nested, zones, shape, children: children, prototype: prototype);

    private static ZType ClassifyType(Zones zones)
    {
        if (zones.IsNested) return ZType.Nested;
        return zones.ElType switch
        {
            ElType.APLBOOL => ZType.Bool,
            ElType.APLSINT => ZType.Byte,
            ElType.APLNCHAR or ElType.APLWCHAR8 or ElType.APLWCHAR16 or ElType.APLWCHAR32 => ZType.Char,
            ElType.APLDOUB => ZType.Double,
            ElType.APLDECF_BID or ElType.APLDECF_DPD => ZType.Decf,
            _ => ZType.Int,
        };
    }

    private static byte[] PackBools(ReadOnlySpan<bool> data)
    {
        int byteCount = (data.Length + 7) / 8;
        var bytes = new byte[byteCount];
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i])
            {
                int byteIdx = i / 8;
                int bitIdx = 7 - (i % 8); // MSB-first
                bytes[byteIdx] |= (byte)(1 << bitIdx);
            }
        }
        return bytes;
    }

    // ─── Convenience factories for ZBus ───

    /// <summary>Empty numeric vector (⍬ / zilde).</summary>
    public static ZValue EmptyNumeric =>
        new(ZType.Int, Zones.Simple(1, ElType.APLLONG), [0], bytes: []);

    /// <summary>Empty character vector ('').</summary>
    public static ZValue EmptyChar =>
        new(ZType.Char, Zones.Simple(1, ElType.APLWCHAR16), [0], bytes: []);

    /// <summary>Create a nested vector of strings from a list of strings.</summary>
    public static ZValue FromStringArray(IReadOnlyList<string> strings)
    {
        var items = new ZValue[strings.Count];
        for (int i = 0; i < strings.Count; i++)
            items[i] = FromChars(strings[i]);
        return Nested(items);
    }
}
