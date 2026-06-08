using System.Runtime.InteropServices;
using System.Text;

namespace ZFormat;

/// <summary>
/// Represents a decoded APL value from a Z buffer.
/// Discriminated by <see cref="Kind"/>. Use the typed accessors to extract data.
/// </summary>
public sealed class ZValue
{
    public ZValueKind Kind { get; }
    public Zones Zones { get; }
    public long[] Shape { get; }

    // Data storage — exactly one is populated depending on Kind
    private readonly byte[]? _bytes;
    private readonly ZValue[]? _children;

    private ZValue(ZValueKind kind, Zones zones, long[] shape, byte[]? bytes = null, ZValue[]? children = null)
    {
        Kind = kind;
        Zones = zones;
        Shape = shape;
        _bytes = bytes;
        _children = children;
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

    /// <summary>Raw data bytes (for simple arrays).</summary>
    public ReadOnlySpan<byte> RawData => _bytes ?? [];

    /// <summary>Children (for nested arrays).</summary>
    public ReadOnlySpan<ZValue> Children => _children ?? [];

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
        // UTF-32 codepoints may require surrogate pairs
        return string.Create(count, span.ToArray(), static (dest, codepoints) =>
        {
            int di = 0;
            for (int i = 0; i < codepoints.Length && di < dest.Length; i++)
            {
                if (codepoints[i] <= 0xFFFF)
                    dest[di++] = (char)codepoints[i];
                else
                {
                    // This path won't fit in pre-allocated length — need surrogate
                    // For now, use replacement character for simplicity
                    dest[di++] = (char)codepoints[i]; // will truncate high bits
                }
            }
        });
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

    // ─── Higher-rank factories ───

    /// <summary>Create a simple array with arbitrary rank and shape.</summary>
    public static ZValue FromSimple(ElType elType, long[] shape, byte[] data)
    {
        var zones = Zones.Simple(shape.Length, elType);
        return new ZValue(ClassifyKind(zones), zones, shape, data);
    }

    /// <summary>Create a higher-rank boolean array (row-major, flat bit packing, no per-row padding).</summary>
    public static ZValue FromBooleans(long[] shape, ReadOnlySpan<bool> data)
    {
        long totalElements = 1;
        foreach (var dim in shape) totalElements *= dim;
        if (data.Length != totalElements)
            throw new ArgumentException($"Expected {totalElements} booleans for shape [{string.Join(",", shape)}]");

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
        return new ZValue(ZValueKind.BoolVector, Zones.Simple(shape.Length, ElType.APLBOOL), shape, bytes);
    }

    /// <summary>Create a higher-rank Int32 array.</summary>
    public static ZValue FromInt32Array(long[] shape, ReadOnlySpan<int> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZValueKind.IntVector, Zones.Simple(shape.Length, ElType.APLLONG), shape, bytes);
    }

    /// <summary>Create a higher-rank double array.</summary>
    public static ZValue FromDoubleArray(long[] shape, ReadOnlySpan<double> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZValueKind.DoubleVector, Zones.Simple(shape.Length, ElType.APLDOUB), shape, bytes);
    }

    /// <summary>Create a higher-rank character array.</summary>
    public static ZValue FromCharArray(long[] shape, string data)
    {
        long totalElements = 1;
        foreach (var dim in shape) totalElements *= dim;
        if (data.Length != totalElements)
            throw new ArgumentException($"Expected {totalElements} characters for shape");

        // Determine width
        int maxCodepoint = 0;
        foreach (char c in data) maxCodepoint = Math.Max(maxCodepoint, c);
        ElType eltype = maxCodepoint <= 255 ? ElType.APLWCHAR8 : (maxCodepoint <= 65535 ? ElType.APLWCHAR16 : ElType.APLWCHAR32);

        byte[] bytes;
        switch (eltype)
        {
            case ElType.APLWCHAR8:
                bytes = new byte[data.Length];
                for (int i = 0; i < data.Length; i++) bytes[i] = (byte)data[i];
                break;
            case ElType.APLWCHAR16:
                bytes = new byte[data.Length * 2];
                var u16 = MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan());
                for (int i = 0; i < data.Length; i++) u16[i] = (ushort)data[i];
                break;
            default: // APLWCHAR32
                bytes = new byte[data.Length * 4];
                var u32 = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
                for (int i = 0; i < data.Length; i++) u32[i] = data[i];
                break;
        }
        return new ZValue(ZValueKind.CharVector, Zones.Simple(shape.Length, eltype), shape, bytes);
    }

    public static ZValue FromString(string s)
    {
        if (s.Length == 0)
            return new ZValue(ZValueKind.CharVector, Zones.Simple(1, ElType.APLWCHAR8), [0], []);

        // Determine minimum character width
        int maxCodepoint = 0;
        foreach (char c in s)
            if (c > maxCodepoint) maxCodepoint = c;

        // Note: doesn't handle surrogate pairs for codepoints > 0xFFFF from C# strings.
        // For full support, iterate Rune by Rune.
        if (maxCodepoint <= 255)
        {
            var bytes = new byte[s.Length];
            for (int i = 0; i < s.Length; i++)
                bytes[i] = (byte)s[i];
            return new ZValue(ZValueKind.CharVector, Zones.Simple(1, ElType.APLWCHAR8), [s.Length], bytes);
        }
        else if (maxCodepoint <= 65535)
        {
            var bytes = new byte[s.Length * 2];
            var span = MemoryMarshal.Cast<byte, ushort>(bytes.AsSpan());
            for (int i = 0; i < s.Length; i++)
                span[i] = s[i];
            return new ZValue(ZValueKind.CharVector, Zones.Simple(1, ElType.APLWCHAR16), [s.Length], bytes);
        }
        else
        {
            // Full UTF-32 path
            var runes = s.EnumerateRunes().ToArray();
            var bytes = new byte[runes.Length * 4];
            var span = MemoryMarshal.Cast<byte, int>(bytes.AsSpan());
            for (int i = 0; i < runes.Length; i++)
                span[i] = runes[i].Value;
            return new ZValue(ZValueKind.CharVector, Zones.Simple(1, ElType.APLWCHAR32), [runes.Length], bytes);
        }
    }

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
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, eltype), [], bytes);
    }

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
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, eltype), [], bytes[..size]);
    }

    public static ZValue FromInt8(sbyte v) =>
        new(ZValueKind.Scalar, Zones.Simple(0, ElType.APLSINT), [], [(byte)v]);

    public static ZValue FromInt16(short v)
    {
        var bytes = new byte[2];
        MemoryMarshal.Cast<byte, short>(bytes.AsSpan())[0] = v;
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLINTG), [], bytes);
    }

    public static ZValue FromInt32(int v)
    {
        var bytes = new byte[4];
        MemoryMarshal.Cast<byte, int>(bytes.AsSpan())[0] = v;
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLLONG), [], bytes);
    }

    public static ZValue FromInt64(long v)
    {
        var bytes = new byte[8];
        MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = v;
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLQUAD), [], bytes);
    }

    public static ZValue FromDouble(double v)
    {
        var bytes = new byte[8];
        MemoryMarshal.Cast<byte, double>(bytes.AsSpan())[0] = v;
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLDOUB), [], bytes);
    }

    /// <summary>
    /// Create a DECF (decimal128 BID) scalar from raw 16-byte representation.
    /// Use this to pass exact 64-bit integers to APL when ⎕FR←1287 is active.
    /// </summary>
    public static ZValue FromDecf(ReadOnlySpan<byte> bid128Bytes)
    {
        if (bid128Bytes.Length != 16)
            throw new ArgumentException("DECF requires exactly 16 bytes (BID128 format)");
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLDECF_BID), [], bid128Bytes.ToArray());
    }

    /// <summary>
    /// Create a DECF scalar encoding a 64-bit integer exactly.
    /// The BID128 format stores the coefficient directly in the low 113 bits for small values.
    /// Exponent=0, sign=0 for positive, coefficient=value.
    /// </summary>
    public static ZValue FromDecfInt64(long v)
    {
        // BID128 layout (simplified for integer values with exponent 0):
        // Bytes 0-7: low 64 bits of coefficient (little-endian)
        // Bytes 8-15: high combination field
        // For exponent=0, positive integer: combination = 0x3040000000000000
        // (exponent biased by 6176, so exp=0 → biased=6176=0x1820, shifted into position)
        var bytes = new byte[16];
        if (v >= 0)
        {
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = v;
            // High word: exponent=0 biased (0x3040 << 48 on the high qword)
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan(8))[0] = 0x3040000000000000L;
        }
        else
        {
            // Negative: set sign bit (bit 127) and store magnitude
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan())[0] = -v;
            MemoryMarshal.Cast<byte, long>(bytes.AsSpan(8))[0] = unchecked((long)0xB040000000000000UL);
        }
        return new ZValue(ZValueKind.Scalar, Zones.Simple(0, ElType.APLDECF_BID), [], bytes);
    }

    /// <summary>
    /// Create a DECF vector from raw 16-byte elements.
    /// </summary>
    public static ZValue FromDecfArray(ReadOnlySpan<byte> bid128Data, int elementCount)
    {
        if (bid128Data.Length != elementCount * 16)
            throw new ArgumentException($"Expected {elementCount * 16} bytes for {elementCount} DECF elements");
        return new ZValue(ZValueKind.DecfVector, Zones.Simple(1, ElType.APLDECF_BID), [elementCount], bid128Data.ToArray());
    }

    /// <summary>Create an integer scalar using the smallest type that fits.</summary>
    public static ZValue FromIntSqueezed(long v) => v switch
    {
        >= 0 and <= 127 => FromInt8((sbyte)v),
        >= -128 and <= 127 => FromInt8((sbyte)v),
        >= short.MinValue and <= short.MaxValue => FromInt16((short)v),
        >= int.MinValue and <= int.MaxValue => FromInt32((int)v),
        _ => FromInt64(v),
    };

    public static ZValue FromBytes(ReadOnlySpan<byte> data) =>
        new(ZValueKind.ByteVector, Zones.Simple(1, ElType.APLSINT), [data.Length], data.ToArray());

    public static ZValue FromBooleans(ReadOnlySpan<bool> data)
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
        return new ZValue(ZValueKind.BoolVector, Zones.Simple(1, ElType.APLBOOL), [data.Length], bytes);
    }

    public static ZValue FromInt32Array(ReadOnlySpan<int> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZValueKind.IntVector, Zones.Simple(1, ElType.APLLONG), [data.Length], bytes);
    }

    public static ZValue FromDoubleArray(ReadOnlySpan<double> data)
    {
        var bytes = MemoryMarshal.AsBytes(data).ToArray();
        return new ZValue(ZValueKind.DoubleVector, Zones.Simple(1, ElType.APLDOUB), [data.Length], bytes);
    }

    public static ZValue Nested(params ZValue[] items)
    {
        if (items.Length == 0)
            throw new ArgumentException("Empty nested arrays crash the interpreter. Use an empty simple array instead.");
        return new ZValue(ZValueKind.Nested, Zones.Nested(1), [items.Length], children: items);
    }

    /// <summary>Create a nested array with explicit rank and shape (for matrices of nested).</summary>
    public static ZValue Nested(long[] shape, ZValue[] items)
    {
        if (items.Length == 0)
            throw new ArgumentException("Empty nested arrays crash the interpreter.");
        return new ZValue(ZValueKind.Nested, Zones.Nested(shape.Length), shape, children: items);
    }

    // ─── Internal factory for ZReader ───

    internal static ZValue FromRaw(Zones zones, long[] shape, byte[] data) =>
        new(ClassifyKind(zones), zones, shape, data);

    internal static ZValue FromNested(Zones zones, long[] shape, ZValue[] children) =>
        new(ZValueKind.Nested, zones, shape, children: children);

    private static ZValueKind ClassifyKind(Zones zones)
    {
        if (zones.IsNested) return ZValueKind.Nested;
        if (zones.Rank == 0) return ZValueKind.Scalar;
        return zones.ElType switch
        {
            ElType.APLBOOL => ZValueKind.BoolVector,
            ElType.APLSINT => ZValueKind.ByteVector,
            ElType.APLNCHAR or ElType.APLWCHAR8 or ElType.APLWCHAR16 or ElType.APLWCHAR32 => ZValueKind.CharVector,
            ElType.APLDOUB => ZValueKind.DoubleVector,
            ElType.APLDECF_BID or ElType.APLDECF_DPD => ZValueKind.DecfVector,
            _ => ZValueKind.IntVector,
        };
    }
}

public enum ZValueKind
{
    Scalar,
    CharVector,
    ByteVector,
    BoolVector,
    IntVector,
    DoubleVector,
    DecfVector,
    Nested,
}
