using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ZFormat;

/// <summary>
/// Zero-copy view over a Z wire format buffer. Must not outlive the ⎕NA call.
///
/// <para>
/// Unlike <see cref="ZValue"/> which copies data from the wire into managed arrays,
/// <c>ZView</c> provides direct span access to the underlying native buffer.
/// It is a <c>ref struct</c> — the compiler prevents it from escaping to the heap,
/// ensuring it cannot outlive the pinned ⎕NA input buffer.
/// </para>
///
/// <para>Usage pattern in a ⎕NA callback:</para>
/// <code>
///   var view = ZView.FromNative(zParam);
///   ReadOnlySpan&lt;int&gt; data = view.SpanInt32(); // zero-copy!
///   // Process data synchronously — do NOT store 'view' anywhere
/// </code>
///
/// <para>For nested arrays, use the indexer to navigate children lazily:</para>
/// <code>
///   var child = view[0]; // returns another ZView over the child pocket
/// </code>
/// </summary>
public unsafe ref struct ZView
{
    private readonly ReadOnlySpan<byte> _buffer; // full Z buffer from header to end
    private readonly int _pocketOffset;          // offset of this pocket's wc within _buffer

    // Cached from parsing the pocket header
    private readonly Zones _zones;
    private readonly int _shapeOffset;   // offset of shape[0] within _buffer
    private readonly int _dataOffset;    // offset of data / first child within _buffer
    private readonly int _rank;

    private ZView(ReadOnlySpan<byte> buffer, int pocketOffset)
    {
        _buffer = buffer;
        _pocketOffset = pocketOffset;

        // Parse pocket header: wc (8 bytes) + zones (8 bytes)
        int zonesOff = pocketOffset + 8;
        _zones = new Zones(MemoryMarshal.Read<long>(buffer[zonesOff..]));
        _rank = _zones.Rank;
        _shapeOffset = pocketOffset + 16; // after wc + zones
        _dataOffset = _shapeOffset + _rank * 8;
    }

    // ═══════════════════════════════════════════════════════════════
    // Construction
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a ZView from a ⎕NA &lt;Z or =Z input pointer.
    /// The pointer follows the self-pointer convention: z_param → [self-ptr][Z payload].
    /// </summary>
    public static ZView FromNative(nint zParam)
    {
        nint payloadPtr = *(nint*)zParam;
        byte* z = (byte*)payloadPtr;
        int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
        var buffer = new ReadOnlySpan<byte>(z, totalSize);
        return new ZView(buffer, 8); // pocket starts after 8-byte Z header
    }

    /// <summary>
    /// Create a ZView from a raw pointer to a Z buffer (no self-pointer indirection).
    /// </summary>
    public static ZView FromDirect(nint bufferPtr)
    {
        byte* z = (byte*)bufferPtr;
        int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
        var buffer = new ReadOnlySpan<byte>(z, totalSize);
        return new ZView(buffer, 8);
    }

    /// <summary>Create a ZView from a managed byte span (e.g., for testing).</summary>
    public static ZView FromSpan(ReadOnlySpan<byte> buffer)
    {
        return new ZView(buffer, 8); // pocket starts after 8-byte Z header
    }

    // ═══════════════════════════════════════════════════════════════
    // Properties (mirror ZValue API)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Coarse element-type family.</summary>
    public ZType Type => _zones.IsNested ? ZType.Nested : ClassifyType(_zones);

    /// <summary>Exact storage element type.</summary>
    public ElType ElType => _zones.ElType;

    /// <summary>Array rank (0=scalar, 1=vector, 2=matrix, etc.).</summary>
    public int Rank => _rank;

    /// <summary>Read shape dimension at given axis. Avoids allocating a shape array.</summary>
    public long Shape(int axis)
    {
        if ((uint)axis >= (uint)_rank)
            throw new ArgumentOutOfRangeException(nameof(axis));
        return MemoryMarshal.Read<long>(_buffer[(_shapeOffset + axis * 8)..]);
    }

    /// <summary>Copy shape to a caller-provided span.</summary>
    public void CopyShapeTo(Span<long> destination)
    {
        if (destination.Length < _rank)
            throw new ArgumentException("Destination too small for shape");
        for (int i = 0; i < _rank; i++)
            destination[i] = MemoryMarshal.Read<long>(_buffer[(_shapeOffset + i * 8)..]);
    }

    /// <summary>Total element count (product of shape dimensions). 1 for scalars.</summary>
    public long ElementCount
    {
        get
        {
            if (_rank == 0) return 1;
            long count = 1;
            for (int i = 0; i < _rank; i++)
                count *= MemoryMarshal.Read<long>(_buffer[(_shapeOffset + i * 8)..]);
            return count;
        }
    }

    /// <summary>Whether this is an empty array (any shape dimension is 0).</summary>
    public bool IsEmpty
    {
        get
        {
            for (int i = 0; i < _rank; i++)
                if (MemoryMarshal.Read<long>(_buffer[(_shapeOffset + i * 8)..]) == 0)
                    return true;
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Zero-copy data access (simple arrays)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Raw data bytes for simple arrays.</summary>
    public ReadOnlySpan<byte> RawData
    {
        get
        {
            int dataBytes = DataByteCount;
            return _buffer.Slice(_dataOffset, dataBytes);
        }
    }

    /// <summary>Zero-copy access as Int8 (APLSINT) span.</summary>
    public ReadOnlySpan<sbyte> SpanInt8()
    {
        if (_zones.ElType != ElType.APLSINT)
            throw new InvalidOperationException($"SpanInt8 requires APLSINT, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, sbyte>(_buffer.Slice(_dataOffset, (int)ElementCount));
    }

    /// <summary>Zero-copy access as Int16 (APLINTG) span.</summary>
    public ReadOnlySpan<short> SpanInt16()
    {
        if (_zones.ElType != ElType.APLINTG)
            throw new InvalidOperationException($"SpanInt16 requires APLINTG, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, short>(_buffer.Slice(_dataOffset, (int)ElementCount * 2));
    }

    /// <summary>Zero-copy access as Int32 (APLLONG) span.</summary>
    public ReadOnlySpan<int> SpanInt32()
    {
        if (_zones.ElType != ElType.APLLONG)
            throw new InvalidOperationException($"SpanInt32 requires APLLONG, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, int>(_buffer.Slice(_dataOffset, (int)ElementCount * 4));
    }

    /// <summary>Zero-copy access as Int64 (APLQUAD) span.</summary>
    public ReadOnlySpan<long> SpanInt64()
    {
        if (_zones.ElType != ElType.APLQUAD)
            throw new InvalidOperationException($"SpanInt64 requires APLQUAD, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, long>(_buffer.Slice(_dataOffset, (int)ElementCount * 8));
    }

    /// <summary>Zero-copy access as Double (APLDOUB) span.</summary>
    public ReadOnlySpan<double> SpanDouble()
    {
        if (_zones.ElType != ElType.APLDOUB)
            throw new InvalidOperationException($"SpanDouble requires APLDOUB, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, double>(_buffer.Slice(_dataOffset, (int)ElementCount * 8));
    }

    /// <summary>Zero-copy access as Char8 (APLWCHAR8/APLNCHAR) span.</summary>
    public ReadOnlySpan<byte> SpanChar8()
    {
        if (_zones.ElType is not (ElType.APLWCHAR8 or ElType.APLNCHAR))
            throw new InvalidOperationException($"SpanChar8 requires APLWCHAR8/APLNCHAR, got {_zones.ElType}");
        return _buffer.Slice(_dataOffset, (int)ElementCount);
    }

    /// <summary>Zero-copy access as Char16 (APLWCHAR16) span.</summary>
    public ReadOnlySpan<ushort> SpanChar16()
    {
        if (_zones.ElType != ElType.APLWCHAR16)
            throw new InvalidOperationException($"SpanChar16 requires APLWCHAR16, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, ushort>(_buffer.Slice(_dataOffset, (int)ElementCount * 2));
    }

    /// <summary>Zero-copy access as Char32 (APLWCHAR32) span.</summary>
    public ReadOnlySpan<uint> SpanChar32()
    {
        if (_zones.ElType != ElType.APLWCHAR32)
            throw new InvalidOperationException($"SpanChar32 requires APLWCHAR32, got {_zones.ElType}");
        return MemoryMarshal.Cast<byte, uint>(_buffer.Slice(_dataOffset, (int)ElementCount * 4));
    }

    /// <summary>Zero-copy access to DECF (BID128) bytes. Returns 16 bytes per element.</summary>
    public ReadOnlySpan<byte> SpanDecf()
    {
        if (_zones.ElType is not (ElType.APLDECF_BID or ElType.APLDECF_DPD))
            throw new InvalidOperationException($"SpanDecf requires APLDECF, got {_zones.ElType}");
        return _buffer.Slice(_dataOffset, (int)ElementCount * 16);
    }

    /// <summary>Read as string (allocates). Valid for all char element types.</summary>
    public string AsString()
    {
        if (!ElTypeInfo.IsChar(_zones.ElType))
            throw new InvalidOperationException($"Not a character array (eltype={_zones.ElType})");

        int count = (int)ElementCount;
        if (count == 0) return "";

        return _zones.ElType switch
        {
            ElType.APLNCHAR or ElType.APLWCHAR8 => AsStringFromChar8(count),
            ElType.APLWCHAR16 => AsStringFromChar16(count),
            ElType.APLWCHAR32 => AsStringFromChar32(count),
            _ => throw new NotSupportedException()
        };
    }

    private string AsStringFromChar8(int count)
    {
        var raw = _buffer.Slice(_dataOffset, count);
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)raw[i];
        return new string(chars);
    }

    private string AsStringFromChar16(int count)
    {
        var raw = MemoryMarshal.Cast<byte, ushort>(_buffer.Slice(_dataOffset, count * 2));
        var chars = new char[count];
        for (int i = 0; i < count; i++)
            chars[i] = (char)raw[i];
        return new string(chars);
    }

    private string AsStringFromChar32(int count)
    {
        var raw = MemoryMarshal.Cast<byte, uint>(_buffer.Slice(_dataOffset, count * 4));
        var sb = new System.Text.StringBuilder(count);
        for (int i = 0; i < count; i++)
            sb.Append(char.ConvertFromUtf32((int)raw[i]));
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════
    // Scalar convenience
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Read a scalar integer value (any integer ElType).</summary>
    public long AsInt64()
    {
        if (_rank != 0) throw new InvalidOperationException("Not a scalar");
        return _zones.ElType switch
        {
            ElType.APLBOOL => _buffer[_dataOffset] != 0 ? 1L : 0L,
            ElType.APLSINT => (sbyte)_buffer[_dataOffset],
            ElType.APLINTG => MemoryMarshal.Read<short>(_buffer[_dataOffset..]),
            ElType.APLLONG => MemoryMarshal.Read<int>(_buffer[_dataOffset..]),
            ElType.APLQUAD => MemoryMarshal.Read<long>(_buffer[_dataOffset..]),
            _ => throw new InvalidOperationException($"Not an integer type: {_zones.ElType}")
        };
    }

    /// <summary>Read a scalar double value.</summary>
    public double AsDouble()
    {
        if (_rank != 0) throw new InvalidOperationException("Not a scalar");
        if (_zones.ElType != ElType.APLDOUB)
            throw new InvalidOperationException($"Not a double: {_zones.ElType}");
        return MemoryMarshal.Read<double>(_buffer[_dataOffset..]);
    }

    // ═══════════════════════════════════════════════════════════════
    // Nested array navigation
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Access a nested child by index. Returns a ZView over the child pocket.
    /// Navigation is lazy — child offsets are computed by scanning preceding siblings.
    /// </summary>
    public ZView this[int index]
    {
        get
        {
            if (!_zones.IsNested)
                throw new InvalidOperationException("Not a nested array");

            long count = ElementCount;
            if (count == 0 && index == 0)
                throw new InvalidOperationException("Empty nested array — use Prototype instead");
            if ((uint)index >= (ulong)count)
                throw new ArgumentOutOfRangeException(nameof(index));

            // Scan forward through child pockets to find the target
            int childOffset = _dataOffset;
            for (int i = 0; i < index; i++)
                childOffset = SkipPocket(childOffset);

            return new ZView(_buffer, childOffset);
        }
    }

    /// <summary>
    /// Access the prototype of an empty nested array.
    /// Returns a ZView over the phantom prototype child.
    /// </summary>
    public ZView Prototype
    {
        get
        {
            if (!_zones.IsNested)
                throw new InvalidOperationException("Not a nested array");
            if (!IsEmpty)
                throw new InvalidOperationException("Only empty nested arrays have an explicit prototype");
            return new ZView(_buffer, _dataOffset);
        }
    }

    /// <summary>Whether this is a nested array with an explicit prototype (i.e., empty nested).</summary>
    public bool HasPrototype => _zones.IsNested && IsEmpty;

    // ═══════════════════════════════════════════════════════════════
    // Conversion to ZValue (when you need to escape the call scope)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Convert this view to an owning ZValue by copying data to managed memory.
    /// Use when you need to persist the value beyond the ⎕NA call.
    /// </summary>
    public ZValue ToZValue()
    {
        // Re-read using the allocating ZReader from the same offset
        int offset = _pocketOffset;
        return ReadPocketCopy(_buffer, ref offset);
    }

    private static ZValue ReadPocketCopy(ReadOnlySpan<byte> buffer, ref int offset)
    {
        long wc = MemoryMarshal.Read<long>(buffer[offset..]);
        offset += 8;
        var zones = new Zones(MemoryMarshal.Read<long>(buffer[offset..]));
        offset += 8;

        int rank = zones.Rank;
        var shape = new long[rank];
        for (int i = 0; i < rank; i++)
        {
            shape[i] = MemoryMarshal.Read<long>(buffer[offset..]);
            offset += 8;
        }

        if (zones.IsNested)
        {
            long elementCount = 1;
            foreach (var dim in shape) elementCount *= dim;

            if (elementCount == 0)
            {
                ZValue? prototype = null;
                if (offset < buffer.Length)
                    prototype = ReadPocketCopy(buffer, ref offset);
                return ZValue.FromNested(zones, shape, [], prototype);
            }

            var children = new ZValue[(int)elementCount];
            for (int i = 0; i < children.Length; i++)
                children[i] = ReadPocketCopy(buffer, ref offset);
            return ZValue.FromNested(zones, shape, children);
        }
        else
        {
            long elementCount = 1;
            foreach (var dim in shape) elementCount *= dim;
            if (rank == 0) elementCount = 1;

            int dataBytes = zones.ElType switch
            {
                ElType.APLBOOL => (int)((elementCount + 7) / 8),
                _ => (int)elementCount * ElTypeInfo.BytesPerElement(zones.ElType),
            };

            int padded = (dataBytes + 7) & ~7;
            var data = buffer.Slice(offset, dataBytes).ToArray();
            offset += padded;

            return ZValue.FromRaw(zones, shape, data);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal helpers
    // ═══════════════════════════════════════════════════════════════

    private int DataByteCount
    {
        get
        {
            long count = ElementCount;
            return _zones.ElType switch
            {
                ElType.APLBOOL => (int)((count + 7) / 8),
                _ => (int)count * ElTypeInfo.BytesPerElement(_zones.ElType),
            };
        }
    }

    /// <summary>Skip past a pocket at the given offset, returning the offset of the next pocket.</summary>
    private int SkipPocket(int offset)
    {
        // Read wc — pocket's in-memory size in qwords (includes 16-byte PocketHeader).
        // On wire, child pockets occupy (wc × 8 - 8) bytes because RefCount is not serialized.
        long wc = MemoryMarshal.Read<long>(_buffer[offset..]);
        int zonesOff = offset + 8;
        var zones = new Zones(MemoryMarshal.Read<long>(_buffer[zonesOff..]));

        if (!zones.IsNested)
        {
            // Simple pocket: on-wire size is wc*8 - 8
            return offset + (int)(wc * 8) - 8;
        }

        // Nested pocket: wc covers root only (header + shape + pointer slots),
        // but children follow inline. Must scan children.
        int rank = zones.Rank;
        int shapeStart = offset + 16;

        long elementCount = 1;
        for (int i = 0; i < rank; i++)
            elementCount *= MemoryMarshal.Read<long>(_buffer[(shapeStart + i * 8)..]);

        // For empty nested, there's 1 prototype child
        int childCount = elementCount == 0 ? 1 : (int)elementCount;
        int childStart = shapeStart + rank * 8;

        int childOffset = childStart;
        for (int i = 0; i < childCount; i++)
            childOffset = SkipPocket(childOffset);

        return childOffset;
    }

    private static ZType ClassifyType(Zones zones)
    {
        if (zones.IsNested) return ZType.Nested;
        return zones.ElType switch
        {
            ElType.APLBOOL => ZType.Bool,
            ElType.APLSINT => ZType.Byte,
            ElType.APLINTG or ElType.APLLONG or ElType.APLQUAD => ZType.Int,
            ElType.APLDOUB => ZType.Double,
            ElType.APLNCHAR or ElType.APLWCHAR8 or ElType.APLWCHAR16 or ElType.APLWCHAR32 => ZType.Char,
            ElType.APLDECF_BID or ElType.APLDECF_DPD => ZType.Decf,
            _ => ZType.Byte,
        };
    }
}
