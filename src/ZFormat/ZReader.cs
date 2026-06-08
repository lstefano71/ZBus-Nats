using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ZFormat;

/// <summary>
/// Deserialises Z wire format buffers into <see cref="ZValue"/> instances.
///
/// Two modes:
/// 1. <see cref="Read(ReadOnlySpan{byte})"/> — from a managed byte span
/// 2. <see cref="ReadFromNative(nint)"/> — from a ⎕NA &lt;Z or =Z input pointer
/// </summary>
public static unsafe class ZReader
{
    private const int ZHeaderSize = 8;

    // ═══════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Deserialise a Z buffer from a byte span.</summary>
    public static ZValue Read(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < ZHeaderSize)
            throw new ArgumentException("Buffer too small for Z header");

        int totalSize = BinaryPrimitives.ReadInt32BigEndian(buffer);
        if (buffer.Length < totalSize)
            throw new ArgumentException($"Buffer length {buffer.Length} < declared size {totalSize}");

        int offset = ZHeaderSize;
        return ReadPocket(buffer, ref offset);
    }

    /// <summary>
    /// Read from a ⎕NA &lt;Z or =Z input pointer.
    /// The pointer follows the self-pointer convention: z_param → [self-ptr][Z payload].
    /// </summary>
    public static ZValue ReadFromNative(nint zParam)
    {
        // Follow the self-pointer to get the Z payload address
        nint payloadPtr = *(nint*)zParam;
        byte* z = (byte*)payloadPtr;

        // Read total size from big-endian header
        int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
        var span = new ReadOnlySpan<byte>(z, totalSize);

        int offset = ZHeaderSize;
        return ReadPocket(span, ref offset);
    }

    /// <summary>
    /// Read from a raw native pointer to a Z buffer (no self-pointer indirection).
    /// Use when you already have the buffer address (e.g., from a >Z result).
    /// </summary>
    public static ZValue ReadDirect(nint bufferPtr)
    {
        byte* z = (byte*)bufferPtr;
        int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
        var span = new ReadOnlySpan<byte>(z, totalSize);

        int offset = ZHeaderSize;
        return ReadPocket(span, ref offset);
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal parsing
    // ═══════════════════════════════════════════════════════════════

    private static ZValue ReadPocket(ReadOnlySpan<byte> buffer, ref int offset)
    {
        if (offset + 16 > buffer.Length)
            throw new InvalidOperationException("Buffer too small for pocket header (wc + zones)");

        long wc = MemoryMarshal.Read<long>(buffer[offset..]);
        offset += 8;
        var zones = new Zones(MemoryMarshal.Read<long>(buffer[offset..]));
        offset += 8;

        int rank = zones.Rank;

        // Read shape
        var shape = new long[rank];
        for (int i = 0; i < rank; i++)
        {
            shape[i] = MemoryMarshal.Read<long>(buffer[offset..]);
            offset += 8;
        }

        if (zones.IsNested)
        {
            // Nested: read children inline
            long elementCount = 1;
            foreach (var dim in shape) elementCount *= dim;

            if (elementCount == 0)
            {
                // Empty nested: the wire format includes 1 prototype child after the shape
                // even though shape says 0 elements. Read it as the prototype.
                ZValue? prototype = null;
                if (offset < buffer.Length)
                    prototype = ReadPocket(buffer, ref offset);
                return ZValue.FromNested(zones, shape, [], prototype);
            }

            var children = new ZValue[(int)elementCount];
            for (int i = 0; i < children.Length; i++)
                children[i] = ReadPocket(buffer, ref offset);

            return ZValue.FromNested(zones, shape, children);
        }
        else
        {
            // Simple: read data bytes
            long elementCount = 1;
            foreach (var dim in shape) elementCount *= dim;
            if (rank == 0) elementCount = 1;

            int dataBytes = zones.ElType switch
            {
                ElType.APLBOOL => (int)((elementCount + 7) / 8),
                _ => (int)elementCount * ElTypeInfo.BytesPerElement(zones.ElType),
            };

            int padded = Pad8(dataBytes);
            var data = buffer.Slice(offset, dataBytes).ToArray();
            offset += padded;

            return ZValue.FromRaw(zones, shape, data);
        }
    }

    private static int Pad8(int n) => (n + 7) & ~7;
}
