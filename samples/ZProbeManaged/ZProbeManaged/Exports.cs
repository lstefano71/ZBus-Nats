using System.Runtime.InteropServices;
using ZFormat;

/// <summary>
/// AOT-compiled DLL that Dyalog APL can load via ⎕NA to test ZReader/ZWriter.
/// 
/// Build: dotnet publish -c Release
/// 
/// APL usage:
///   dll←'path\to\ZProbeManaged'
///   'zecho'  ⎕NA dll,'|z_echo =Z'
///   'zstr'   ⎕NA dll,'|z_make_string >Z'
///   'zint'   ⎕NA dll,'|z_make_int >Z'
///   'znest'  ⎕NA dll,'|z_make_nested >Z'
///   'zbool'  ⎕NA dll,'|z_make_bool_matrix >Z'
///   'zimat'  ⎕NA dll,'|z_make_int_matrix >Z'
///   'zdbl'   ⎕NA dll,'|z_make_double_cube >Z'
///   'zchr'   ⎕NA dll,'|z_make_char_matrix >Z'
///   'zdecf'  ⎕NA dll,'|z_make_decf >Z'
///   'zdeep'  ⎕NA dll,'|z_make_deep_nested >Z'
///   
///   zecho ⊂'hello'    ⍝ echoes back the input
///   zstr 0            ⍝ returns 'ZFormat works!'
///   zint 0            ⍝ returns 42
///   znest 0           ⍝ returns (0 'OK')
/// </summary>
public static unsafe class Exports
{
    /// <summary>
    /// Echo: reads the input Z buffer with ZReader, then writes it back with ZWriter.
    /// This validates full round-trip through both reader and writer.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "z_echo")]
    public static int ZEcho(nint* zParam)
    {
        try
        {
            // Read input: =Z convention — self-pointer at *zParam, payload at **zParam
            nint payloadPtr = *zParam;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);

            // Deserialise
            var value = ZReader.Read(span);

            // Re-serialise and write to output
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a string via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_string")]
    public static int ZMakeString(nint* zParam)
    {
        try
        {
            var value = ZValue.FromChars("ZFormat works!");
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce an integer scalar via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_int")]
    public static int ZMakeInt(nint* zParam)
    {
        try
        {
            var value = ZValue.FromInt(42);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a nested array (0 'OK') via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_nested")]
    public static int ZMakeNested(nint* zParam)
    {
        try
        {
            var value = ZValue.Nested(
                ZValue.FromInt(0),
                ZValue.FromChars("OK"));
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a 2x4 boolean matrix via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_bool_matrix")]
    public static int ZMakeBoolMatrix(nint* zParam)
    {
        try
        {
            var value = ZValue.FromBools([2, 4], [true, false, true, false, false, true, false, true]);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a 2x3 integer matrix via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_int_matrix")]
    public static int ZMakeIntMatrix(nint* zParam)
    {
        try
        {
            var value = ZValue.FromInt32Array([2, 3], [1, 2, 3, 4, 5, 6]);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a 2x2x2 double cube via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_double_cube")]
    public static int ZMakeDoubleCube(nint* zParam)
    {
        try
        {
            var value = ZValue.FromDoubles([2, 2, 2], [1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5]);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a 2x4 Greek character matrix via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_char_matrix")]
    public static int ZMakeCharMatrix(nint* zParam)
    {
        try
        {
            var value = ZValue.FromChars([2, 4], "αβγδεζηθ");
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce an exact DECF scalar via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_decf")]
    public static int ZMakeDecf(nint* zParam)
    {
        try
        {
            var value = ZValue.FromDecfInt64(9007199254740993L);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Produce a deeper nested structure via >Z.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_make_deep_nested")]
    public static int ZMakeDeepNested(nint* zParam)
    {
        try
        {
            var value = ZValue.Nested(
                ZValue.FromInt(0),
                ZValue.Nested(
                    ZValue.FromInt(1),
                    ZValue.FromChars("A")),
                ZValue.Nested(
                    ZValue.FromInt(2),
                    ZValue.Nested(
                        ZValue.FromInt(3),
                        ZValue.FromChars("B"))));
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// The interpreter calls this to free >Z / =Z output buffers.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "FreeUsedDyalogResult")]
    public static int FreeUsedDyalogResult(nint ptr)
    {
        if (ptr != 0)
            NativeMemory.Free((void*)ptr);
        return 1;
    }

    // ═══════════════════════════════════════════════════════════════
    // Squeeze experiments — produce same value at different widths
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Produce 42 as APLSINT (1 byte).</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_int_sint")]
    public static int ZIntSint(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInt8(42));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Produce 42 as APLINTG (2 bytes).</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_int_intg")]
    public static int ZIntIntg(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInt16(42));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Produce 42 as APLLONG (4 bytes).</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_int_long")]
    public static int ZIntLong(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInt32(42));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Produce 42 as auto-squeezed.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_int_squeezed")]
    public static int ZIntSqueezed(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInt(42));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Produce a vector [1 2 3] as APLSINT (1 byte each).</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_vec_sint")]
    public static int ZVecSint(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInts([1, 2, 3]));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Produce a vector [1 2 3] as APLLONG (4 bytes each) — deliberately oversized.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_vec_long")]
    public static int ZVecLong(nint* zParam)
    {
        try
        {
            ZWriter.WriteToNative((nint)zParam, ZValue.FromInt32Array([1, 2, 3]));
            return 0;
        }
        catch { return -1; }
    }

    /// <summary>Echo back input, report what ⎕DR the caller sent by passing through unchanged.</summary>
    [UnmanagedCallersOnly(EntryPoint = "z_echo_unchanged")]
    public static int ZEchoUnchanged(nint* zParam)
    {
        try
        {
            nint payloadPtr = *zParam;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);
            var value = ZReader.Read(span);
            ZWriter.WriteToNative((nint)zParam, value);
            return 0;
        }
        catch { return -1; }
    }

    // ═══════════════════════════════════════════════════════════════
    // Prototype experiment — hex-dump raw Z input bytes
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Receives =Z input, hex-dumps raw bytes to a temp file, and returns the file path as a string.
    /// Use: 'zdump' ⎕NA dll,'|z_dump_input =Z'
    ///      zdump ⊂value
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "z_dump_input")]
    public static int ZDumpInput(nint* zParam)
    {
        try
        {
            nint payloadPtr = *zParam;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];

            // Build hex dump
            var sb = new System.Text.StringBuilder(totalSize * 3 + totalSize / 8 * 20);
            for (int i = 0; i < totalSize; i++)
            {
                if (i % 16 == 0)
                    sb.AppendFormat("{0:X8}  ", i);
                sb.AppendFormat("{0:X2} ", z[i]);
                if (i % 16 == 7) sb.Append(' ');
                if (i % 16 == 15 || i == totalSize - 1)
                    sb.AppendLine();
            }

            // Also decode and show structure
            var span = new ReadOnlySpan<byte>(z, totalSize);
            try
            {
                var value = ZReader.Read(span);
                sb.AppendLine("---");
                sb.AppendFormat("Type={0} Shape=[{1}] ElCount={2}\n",
                    value.Type, string.Join(",", value.Shape), value.ElementCount);
                if (value.Type == ZType.Nested)
                {
                    for (int i = 0; i < value.ElementCount; i++)
                    {
                        var child = value[i];
                        sb.AppendFormat("  [{0}] Type={1} Shape=[{2}] ElCount={3}\n",
                            i, child.Type, string.Join(",", child.Shape), child.ElementCount);
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("--- DECODE FAILED: " + ex.Message);
            }

            // Return the dump as a char vector
            var result = ZValue.FromChars(sb.ToString());
            ZWriter.WriteToNative((nint)zParam, result);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Receives =Z input and returns a diagnostic string with type, shape, zones info.
    /// Use: 'zinfo' ⎕NA dll,'|z_info =Z'
    ///      zinfo ⊂value
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "z_info")]
    public static int ZInfo(nint* zParam)
    {
        try
        {
            nint payloadPtr = *zParam;
            byte* z = (byte*)payloadPtr;
            int totalSize = (z[0] << 24) | (z[1] << 16) | (z[2] << 8) | z[3];
            var span = new ReadOnlySpan<byte>(z, totalSize);

            // Read raw zones qword (offset 8 in the Z payload, after the wc)
            long zonesRaw = 0;
            if (totalSize >= 16)
            {
                for (int i = 0; i < 8; i++)
                    zonesRaw |= (long)z[8 + i] << (i * 8);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("size={0} zones=0x{1:X16}\n", totalSize, zonesRaw);

            try
            {
                var value = ZReader.Read(span);
                sb.AppendFormat("Type={0} Shape=[{1}] ElCount={2}\n",
                    value.Type, string.Join(",", value.Shape), value.ElementCount);

                if (value.Type == ZType.Nested)
                {
                    for (int i = 0; i < value.ElementCount; i++)
                    {
                        var child = value[i];
                        sb.AppendFormat("  [{0}] Type={1} Shape=[{2}] ElCount={3}",
                            i, child.Type, string.Join(",", child.Shape), child.ElementCount);
                        if (child.Type == ZType.Char)
                            sb.AppendFormat(" data=\"{0}\"", child.AsString());
                        else if (child.Type == ZType.Int && child.Shape.Length == 0)
                            sb.AppendFormat(" data={0}", child.SpanInt32()[0]);
                        sb.AppendLine();
                    }
                }
                else if (value.Type == ZType.Int)
                {
                    var ints = value.SpanInt32();
                    sb.Append("data=");
                    for (int i = 0; i < Math.Min(ints.Length, 20); i++)
                        sb.AppendFormat("{0} ", ints[i]);
                    sb.AppendLine();
                }
                else if (value.Type == ZType.Char)
                {
                    sb.AppendFormat("data=\"{0}\"\n", value.AsString());
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("DECODE ERROR: " + ex.Message);
            }

            var result = ZValue.FromChars(sb.ToString());
            ZWriter.WriteToNative((nint)zParam, result);
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Direct Z buffer construction (bypassing ZWriter validation)
    // for research purposes
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Write a raw empty nested Z buffer (shape=[0], no prototype child) to >Z output.
    /// This BYPASSES ZWriter validation to test if the interpreter actually crashes.
    /// Use: 'zemptynest' ⎕NA dll,'|z_empty_nested >Z'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "z_empty_nested")]
    public static int ZEmptyNested(nint* zParam)
    {
        try
        {
            // Manually build: 32 bytes total
            // [0-3] size (BE): 0x00000020 = 32
            // [4-7] flags (BE): 0x000000A4
            // [8-15] wc = 4 (header16 + zones8 + rank8 + 0*8 = 32 / 8 = 4)
            // [16-23] zones: TYPEGEN|rank1|APLPNTR = 0x0617
            // [24-31] shape[0] = 0
            int totalSize = 32;
            byte* buf = (byte*)NativeMemory.AllocZeroed((nuint)totalSize);

            // Z header (big-endian)
            buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x00; buf[3] = 0x20; // size=32
            buf[4] = 0x00; buf[5] = 0x00; buf[6] = 0x00; buf[7] = 0xA4; // flags

            // wc (little-endian qword)
            buf[8] = 0x04; // wc=4

            // zones (little-endian qword): TYPEGEN|rank1|APLPNTR = 0x0617
            buf[16] = 0x17; buf[17] = 0x06;

            // shape[0] = 0 (already zero from AllocZeroed)

            // Write to output pointer
            *(nint*)zParam = (nint)buf;
            return 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Write a raw empty nested Z buffer WITH a prototype child (empty char vector).
    /// Mimics what the interpreter itself sends for 0⍴⊂''.
    /// Use: 'zemptyproto' ⎕NA dll,'|z_empty_nested_with_proto >Z'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "z_empty_nested_with_proto")]
    public static int ZEmptyNestedWithProto(nint* zParam)
    {
        try
        {
            // Replicate the exact bytes the interpreter sends for 0⍴⊂''
            // (from our hex dump above):
            // 00000000  00 00 00 38 00 00 00 A4  05 00 00 00 00 00 00 00
            // 00000010  17 06 00 00 00 00 00 00  00 00 00 00 00 00 00 00
            // 00000020  04 00 00 00 00 00 00 00  1F 27 00 00 00 00 00 00
            // 00000030  00 00 00 00 00 00 00 00
            int totalSize = 0x38; // 56 bytes
            byte* buf = (byte*)NativeMemory.AllocZeroed((nuint)totalSize);

            // Z header
            buf[0] = 0x00; buf[1] = 0x00; buf[2] = 0x00; buf[3] = 0x38; // size=56
            buf[4] = 0x00; buf[5] = 0x00; buf[6] = 0x00; buf[7] = 0xA4; // flags

            // Root wc=5
            buf[8] = 0x05;

            // Root zones: TYPEGEN|rank1|APLPNTR = 0x0617
            buf[16] = 0x17; buf[17] = 0x06;

            // Root shape[0] = 0 (already zero)

            // Prototype child: empty char vector
            // child wc=4
            buf[32] = 0x04;
            // child zones: TYPESIMPLE|rank1|APLWCHAR8|squoze = 0x271F
            buf[40] = 0x1F; buf[41] = 0x27;
            // child shape[0] = 0 (already zero)

            *(nint*)zParam = (nint)buf;
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}
