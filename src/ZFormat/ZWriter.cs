using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ZFormat;

/// <summary>
/// Serialises <see cref="ZValue"/> instances into Z wire format buffers.
///
/// Two modes:
/// 1. <see cref="ToBytes"/> — returns a managed byte[] (for testing, storage, network)
/// 2. <see cref="WriteToNative"/> — allocates native memory and sets the ⎕NA output pointer
///
/// The native mode is designed for use in DLLs loaded by Dyalog APL via ⎕NA.
/// Memory is allocated with NativeMemory.AllocZeroed and freed by the interpreter
/// via FreeUsedDyalogResult.
/// </summary>
public static unsafe class ZWriter
{
    private const int ZHeaderSize = 8;
    private const uint ZFlags = 0x000000A4;

    // ═══════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Serialise a ZValue to a byte array.</summary>
    public static byte[] ToBytes(ZValue value)
    {
        int payloadSize = ComputePayloadSize(value);
        int totalSize = ZHeaderSize + payloadSize;
        var buf = new byte[totalSize];

        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(0), totalSize);
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(4), (int)ZFlags);

        int offset = ZHeaderSize;
        WritePayload(buf, ref offset, value);

        if (offset != totalSize)
            throw new InvalidOperationException($"ZWriter bug: wrote {offset}, expected {totalSize}");

        return buf;
    }

    /// <summary>Write a ZValue to a >Z or =Z output pointer (native memory).</summary>
    public static void WriteToNative(nint zResult, ZValue value)
    {
        int payloadSize = ComputePayloadSize(value);
        int totalSize = ZHeaderSize + payloadSize;

        byte* buf = (byte*)NativeMemory.AllocZeroed((nuint)totalSize);
        try
        {
            buf[0] = (byte)((totalSize >> 24) & 0xFF);
            buf[1] = (byte)((totalSize >> 16) & 0xFF);
            buf[2] = (byte)((totalSize >> 8) & 0xFF);
            buf[3] = (byte)(totalSize & 0xFF);
            buf[4] = 0; buf[5] = 0; buf[6] = 0; buf[7] = 0xA4;

            int offset = ZHeaderSize;
            WritePayloadUnsafe(buf, ref offset, value);

            *(nint*)zResult = (nint)buf;
        }
        catch
        {
            NativeMemory.Free(buf);
            *(nint*)zResult = 0;
            throw;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Size Calculation
    // ═══════════════════════════════════════════════════════════════

    private static int ComputePayloadSize(ZValue value)
    {
        if (value.Kind == ZValueKind.Nested)
            return ComputeNestedPayloadSize(value);

        int rank = value.Zones.Rank;
        int dataBytes = ComputeDataBytes(value);
        int padded = Pad8(dataBytes);
        // wc(8) + zones(8) + rank×shape(8) + data_padded
        return 8 + 8 + rank * 8 + padded;
    }

    private static int ComputeDataBytes(ZValue value)
    {
        long count = value.ElementCount;
        return value.Zones.ElType switch
        {
            ElType.APLBOOL => (int)((count + 7) / 8),
            _ => (int)count * ElTypeInfo.BytesPerElement(value.Zones.ElType),
        };
    }

    private static int ComputeNestedPayloadSize(ZValue value)
    {
        int rank = value.Zones.Rank;
        int rootHeader = 8 + 8 + rank * 8; // wc + zones + shape
        int childrenSize = 0;
        foreach (var child in value.Children)
            childrenSize += ComputePayloadSize(child);
        return rootHeader + childrenSize;
    }

    // ═══════════════════════════════════════════════════════════════
    // Managed byte[] Writer
    // ═══════════════════════════════════════════════════════════════

    private static void WritePayload(byte[] buf, ref int offset, ZValue value)
    {
        if (value.Kind == ZValueKind.Nested)
        {
            WriteNested(buf, ref offset, value);
            return;
        }

        int rank = value.Zones.Rank;
        int dataBytes = ComputeDataBytes(value);
        int padded = Pad8(dataBytes);
        long wc = (16 + 8 + rank * 8 + padded) / 8;

        WriteInt64(buf, ref offset, wc);
        WriteInt64(buf, ref offset, value.Zones.Value);
        foreach (var dim in value.Shape)
            WriteInt64(buf, ref offset, dim);

        // Write raw data
        value.RawData[..dataBytes].CopyTo(buf.AsSpan(offset));
        offset += padded;
    }

    private static void WriteNested(byte[] buf, ref int offset, ZValue value)
    {
        int rank = value.Zones.Rank;
        long elementCount = value.ElementCount;
        long rootWc = (16 + 8 + rank * 8 + elementCount * 8) / 8;

        WriteInt64(buf, ref offset, rootWc);
        WriteInt64(buf, ref offset, value.Zones.Value);
        foreach (var dim in value.Shape)
            WriteInt64(buf, ref offset, dim);

        foreach (var child in value.Children)
            WritePayload(buf, ref offset, child);
    }

    private static void WriteInt64(byte[] buf, ref int offset, long value)
    {
        MemoryMarshal.Write(buf.AsSpan(offset), in value);
        offset += 8;
    }

    // ═══════════════════════════════════════════════════════════════
    // Unsafe (native buffer) Writer
    // ═══════════════════════════════════════════════════════════════

    private static void WritePayloadUnsafe(byte* buf, ref int offset, ZValue value)
    {
        if (value.Kind == ZValueKind.Nested)
        {
            WriteNestedUnsafe(buf, ref offset, value);
            return;
        }

        int rank = value.Zones.Rank;
        int dataBytes = ComputeDataBytes(value);
        int padded = Pad8(dataBytes);
        long wc = (16 + 8 + rank * 8 + padded) / 8;

        *(long*)(buf + offset) = wc; offset += 8;
        *(long*)(buf + offset) = value.Zones.Value; offset += 8;
        foreach (var dim in value.Shape)
        {
            *(long*)(buf + offset) = dim; offset += 8;
        }

        value.RawData[..dataBytes].CopyTo(new Span<byte>(buf + offset, dataBytes));
        offset += padded;
    }

    private static void WriteNestedUnsafe(byte* buf, ref int offset, ZValue value)
    {
        int rank = value.Zones.Rank;
        long elementCount = value.ElementCount;
        long rootWc = (16 + 8 + rank * 8 + elementCount * 8) / 8;

        *(long*)(buf + offset) = rootWc; offset += 8;
        *(long*)(buf + offset) = value.Zones.Value; offset += 8;
        foreach (var dim in value.Shape)
        {
            *(long*)(buf + offset) = dim; offset += 8;
        }

        foreach (var child in value.Children)
            WritePayloadUnsafe(buf, ref offset, child);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static int Pad8(int n) => (n + 7) & ~7;
}
