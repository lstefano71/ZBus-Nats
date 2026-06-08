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
            var value = ZValue.FromString("ZFormat works!");
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
            var value = ZValue.FromIntSqueezed(42);
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
                ZValue.FromIntSqueezed(0),
                ZValue.FromString("OK"));
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
            var value = ZValue.FromBooleans([2, 4], [true, false, true, false, false, true, false, true]);
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
            var value = ZValue.FromDoubleArray([2, 2, 2], [1.5, 2.5, 3.5, 4.5, 5.5, 6.5, 7.5, 8.5]);
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
            var value = ZValue.FromCharArray([2, 4], "αβγδεζηθ");
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
                ZValue.FromIntSqueezed(0),
                ZValue.Nested(
                    ZValue.FromIntSqueezed(1),
                    ZValue.FromString("A")),
                ZValue.Nested(
                    ZValue.FromIntSqueezed(2),
                    ZValue.Nested(
                        ZValue.FromIntSqueezed(3),
                        ZValue.FromString("B"))));
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
}
