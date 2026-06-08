using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Xunit;

namespace ZFormat.Tests;

/// <summary>
/// Golden-byte tests validated against real Dyalog 20.0 interpreter Z dumps.
/// These hex patterns were captured by a C probe DLL using =Z echo.
/// </summary>
public class ZRoundTripTests
{
    // ═══════════════════════════════════════════════════════════════
    // Writer tests — verify exact byte output
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_AsciiString_MatchesInterpreterDump()
    {
        // Interpreter serialises 'hello' as APLWCHAR8, 1 byte/char
        var buf = ZWriter.ToBytes(ZValue.FromString("hello"));

        Assert.Equal(40, buf.Length);
        Assert.Equal(40, BinaryPrimitives.ReadInt32BigEndian(buf));
        Assert.Equal(0xA4, BinaryPrimitives.ReadInt32BigEndian(buf.AsSpan(4)));

        long wc = MemoryMarshal.Read<long>(buf.AsSpan(8));
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));

        Assert.Equal(5L, wc);       // (16+8+8+8)/8
        Assert.Equal(0x271FL, zones); // TYPESIMPLE|rank1|APLWCHAR8|squoze
        Assert.Equal(5L, shape);

        // Data: raw codepoints
        Assert.Equal((byte)'h', buf[32]);
        Assert.Equal((byte)'e', buf[33]);
        Assert.Equal((byte)'l', buf[34]);
        Assert.Equal((byte)'l', buf[35]);
        Assert.Equal((byte)'o', buf[36]);
        Assert.Equal(0, buf[37]); // padding
    }

    [Fact]
    public void Write_LatinString_StoresRawCodepoints()
    {
        // 'café' — all codepoints ≤ 255, uses APLWCHAR8
        // 'é' = U+00E9 stored as byte 0xE9 (NOT UTF-8)
        var buf = ZWriter.ToBytes(ZValue.FromString("café"));

        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x271FL, zones); // APLWCHAR8

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(4L, shape);

        Assert.Equal(0x63, buf[32]); // 'c'
        Assert.Equal(0x61, buf[33]); // 'a'
        Assert.Equal(0x66, buf[34]); // 'f'
        Assert.Equal(0xE9, buf[35]); // 'é' as raw codepoint, NOT UTF-8
    }

    [Fact]
    public void Write_GreekString_UsesAPLWCHAR16()
    {
        // 'αβγ' — codepoints > 255, uses APLWCHAR16
        var buf = ZWriter.ToBytes(ZValue.FromString("αβγ"));

        Assert.Equal(40, buf.Length); // 8+8+8+8+8(6 bytes padded to 8)

        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x281FL, zones); // TYPESIMPLE|rank1|APLWCHAR16|squoze

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(3L, shape);

        // α=U+03B1 → LE bytes: B1 03
        Assert.Equal(0xB1, buf[32]);
        Assert.Equal(0x03, buf[33]);
        // β=U+03B2 → B2 03
        Assert.Equal(0xB2, buf[34]);
        Assert.Equal(0x03, buf[35]);
        // γ=U+03B3 → B3 03
        Assert.Equal(0xB3, buf[36]);
        Assert.Equal(0x03, buf[37]);
    }

    [Fact]
    public void Write_Int32Scalar()
    {
        var buf = ZWriter.ToBytes(ZValue.FromInt32(42));

        Assert.Equal(32, buf.Length);
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x240FL, zones); // TYPESIMPLE|rank0|APLLONG|squoze

        int val = MemoryMarshal.Read<int>(buf.AsSpan(24));
        Assert.Equal(42, val);
    }

    [Fact]
    public void Write_IntSqueezed_FitsInByte()
    {
        var buf = ZWriter.ToBytes(ZValue.FromIntSqueezed(42));
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x220FL, zones); // APLSINT (1 byte)
        Assert.Equal((byte)42, buf[24]);
    }

    [Fact]
    public void Write_IntSqueezed_RequiresInt16()
    {
        var buf = ZWriter.ToBytes(ZValue.FromIntSqueezed(1000));
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x230FL, zones); // APLINTG (2 bytes)
        short val = MemoryMarshal.Read<short>(buf.AsSpan(24));
        Assert.Equal(1000, val);
    }

    [Fact]
    public void Write_IntSqueezed_RequiresInt32()
    {
        var buf = ZWriter.ToBytes(ZValue.FromIntSqueezed(100000));
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x240FL, zones); // APLLONG (4 bytes)
    }

    [Fact]
    public void Write_Double()
    {
        var buf = ZWriter.ToBytes(ZValue.FromDouble(3.14));
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x250FL, zones); // APLDOUB

        double val = MemoryMarshal.Read<double>(buf.AsSpan(24));
        Assert.Equal(3.14, val);
    }

    [Fact]
    public void Write_BoolVector_MSBFirst()
    {
        // 0 1 0 1 0 1 0 1 → byte 0x55
        var bools = new bool[] { false, true, false, true, false, true, false, true };
        var buf = ZWriter.ToBytes(ZValue.FromBooleans(bools));

        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x211FL, zones); // APLBOOL|rank1

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(8L, shape);

        Assert.Equal(0x55, buf[32]); // MSB-first bit packing
    }

    [Fact]
    public void Write_BoolVector_TwoBits()
    {
        // 1 0 → byte 0x80
        var bools = new bool[] { true, false };
        var buf = ZWriter.ToBytes(ZValue.FromBooleans(bools));

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(2L, shape);
        Assert.Equal(0x80, buf[32]);
    }

    [Fact]
    public void Write_NestedVector_IntAndString()
    {
        // (42 'hi') — matches probe dump exactly
        var val = ZValue.Nested(ZValue.FromIntSqueezed(42), ZValue.FromString("hi"));
        var buf = ZWriter.ToBytes(val);

        Assert.Equal(88, buf.Length);

        // Root
        long rootWc = MemoryMarshal.Read<long>(buf.AsSpan(8));
        long rootZones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        long rootShape = MemoryMarshal.Read<long>(buf.AsSpan(24));

        Assert.Equal(6L, rootWc);       // (16+8+8+2*8)/8
        Assert.Equal(0x0617L, rootZones); // TYPEGEN|rank1|APLPNTR
        Assert.Equal(2L, rootShape);

        // Child 0: APLSINT scalar 42
        long c0Zones = MemoryMarshal.Read<long>(buf.AsSpan(40));
        Assert.Equal(0x220FL, c0Zones);
        Assert.Equal((byte)42, buf[48]);

        // Child 1: APLWCHAR8 'hi'
        long c1Zones = MemoryMarshal.Read<long>(buf.AsSpan(64));
        Assert.Equal(0x271FL, c1Zones);
        long c1Shape = MemoryMarshal.Read<long>(buf.AsSpan(72));
        Assert.Equal(2L, c1Shape);
        Assert.Equal((byte)'h', buf[80]);
        Assert.Equal((byte)'i', buf[81]);
    }

    [Fact]
    public void Write_ByteVector()
    {
        var buf = ZWriter.ToBytes(ZValue.FromBytes([10, 20, 30]));

        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x221FL, zones); // APLSINT|rank1

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(3L, shape);

        Assert.Equal(10, buf[32]);
        Assert.Equal(20, buf[33]);
        Assert.Equal(30, buf[34]);
    }

    // ═══════════════════════════════════════════════════════════════
    // Reader tests — verify round-trip
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Read_AsciiString()
    {
        var buf = ZWriter.ToBytes(ZValue.FromString("hello"));
        var result = ZReader.Read(buf);

        Assert.Equal(ZValueKind.CharVector, result.Kind);
        Assert.Equal(ElType.APLWCHAR8, result.Zones.ElType);
        Assert.Equal("hello", result.AsString());
    }

    [Fact]
    public void Read_LatinString()
    {
        var buf = ZWriter.ToBytes(ZValue.FromString("café"));
        var result = ZReader.Read(buf);
        Assert.Equal("café", result.AsString());
    }

    [Fact]
    public void Read_GreekString()
    {
        var buf = ZWriter.ToBytes(ZValue.FromString("αβγ"));
        var result = ZReader.Read(buf);
        Assert.Equal("αβγ", result.AsString());
        Assert.Equal(ElType.APLWCHAR16, result.Zones.ElType);
    }

    [Fact]
    public void Read_IntScalar()
    {
        var buf = ZWriter.ToBytes(ZValue.FromInt32(42));
        var result = ZReader.Read(buf);

        Assert.Equal(ZValueKind.Scalar, result.Kind);
        Assert.Equal(42L, result.AsInt64());
    }

    [Fact]
    public void Read_SqueezedInt()
    {
        var buf = ZWriter.ToBytes(ZValue.FromIntSqueezed(42));
        var result = ZReader.Read(buf);
        Assert.Equal(42L, result.AsInt64());
        Assert.Equal(ElType.APLSINT, result.Zones.ElType);
    }

    [Fact]
    public void Read_Double()
    {
        var buf = ZWriter.ToBytes(ZValue.FromDouble(3.14));
        var result = ZReader.Read(buf);
        Assert.Equal(3.14, result.AsDouble());
    }

    [Fact]
    public void Read_BoolVector()
    {
        var bools = new bool[] { true, false, true, true, false, false, true, false };
        var buf = ZWriter.ToBytes(ZValue.FromBooleans(bools));
        var result = ZReader.Read(buf);

        var ints = result.AsInt64Array();
        Assert.Equal(8, ints.Length);
        Assert.Equal([1, 0, 1, 1, 0, 0, 1, 0], ints);
    }

    [Fact]
    public void Read_ByteVector()
    {
        var buf = ZWriter.ToBytes(ZValue.FromBytes([1, 2, 3, 4, 5]));
        var result = ZReader.Read(buf);

        Assert.Equal(ZValueKind.ByteVector, result.Kind);
        Assert.Equal(5L, result.Shape[0]);
        var ints = result.AsInt64Array();
        Assert.Equal([1, 2, 3, 4, 5], ints);
    }

    [Fact]
    public void Read_NestedVector()
    {
        var original = ZValue.Nested(
            ZValue.FromIntSqueezed(42),
            ZValue.FromString("hi"));
        var buf = ZWriter.ToBytes(original);
        var result = ZReader.Read(buf);

        Assert.Equal(ZValueKind.Nested, result.Kind);
        Assert.Equal(2, result.Children.Length);
        Assert.Equal(42L, result.Children[0].AsInt64());
        Assert.Equal("hi", result.Children[1].AsString());
    }

    [Fact]
    public void Read_EmptyString()
    {
        var buf = ZWriter.ToBytes(ZValue.FromString(""));
        var result = ZReader.Read(buf);

        Assert.Equal("", result.AsString());
        Assert.Equal(0L, result.Shape[0]);
    }

    // ═══════════════════════════════════════════════════════════════
    // Golden bytes from interpreter — parse real captured dumps
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Read_InterpreterDump_Hello()
    {
        // Exact bytes from probe: 'hello' as APLWCHAR8
        byte[] dump = [
            0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0xA4,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x27, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x68, 0x65, 0x6C, 0x6C, 0x6F, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal("hello", result.AsString());
        Assert.Equal(ElType.APLWCHAR8, result.Zones.ElType);
    }

    [Fact]
    public void Read_InterpreterDump_Cafe()
    {
        // 'café' — é stored as 0xE9 (raw codepoint, not UTF-8)
        byte[] dump = [
            0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0xA4,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x27, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x63, 0x61, 0x66, 0xE9, 0x00, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal("café", result.AsString());
    }

    [Fact]
    public void Read_InterpreterDump_Greek()
    {
        // 'αβγ' as APLWCHAR16
        byte[] dump = [
            0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0xA4,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0xB1, 0x03, 0xB2, 0x03, 0xB3, 0x03, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal("αβγ", result.AsString());
        Assert.Equal(ElType.APLWCHAR16, result.Zones.ElType);
    }

    [Fact]
    public void Read_InterpreterDump_Emoji()
    {
        // '😀' as APLWCHAR32 scalar (rank-0!)
        // U+1F600 = 0x0001F600, stored LE as 00 F6 01 00
        string emoji = char.ConvertFromUtf32(0x1F600);
        byte[] dump = [
            0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xA4,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0F, 0x29, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0xF6, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(ZValueKind.Scalar, result.Kind);
        Assert.Equal(ElType.APLWCHAR32, result.Zones.ElType);
        Assert.Equal(0, result.Zones.Rank);
        Assert.Equal(emoji, result.AsString());
    }

    [Fact]
    public void Write_EmojiString_UsesAPLWCHAR32SingleCodepoint()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        var buf = ZWriter.ToBytes(ZValue.FromString(emoji));

        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x291FL, zones); // TYPESIMPLE|rank1|APLWCHAR32|squoze

        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(1L, shape);
        Assert.Equal([0x00, 0xF6, 0x01, 0x00], buf[32..36]);
    }

    [Fact]
    public void RoundTrip_EmojiString()
    {
        string emoji = char.ConvertFromUtf32(0x1F600);
        var decoded = ZReader.Read(ZWriter.ToBytes(ZValue.FromString(emoji)));

        Assert.Equal(ElType.APLWCHAR32, decoded.Zones.ElType);
        Assert.Equal(emoji, decoded.AsString());
    }

    [Fact]
    public void Read_InterpreterDump_Int42()
    {
        // Integer 42 as APLSINT
        byte[] dump = [
            0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xA4,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0F, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(42L, result.AsInt64());
        Assert.Equal(ElType.APLSINT, result.Zones.ElType);
    }

    [Fact]
    public void Read_InterpreterDump_Float314()
    {
        // 3.14 as APLDOUB
        byte[] dump = [
            0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xA4,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0F, 0x25, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x85, 0xEB, 0x51, 0xB8, 0x1E, 0x09, 0x40
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(3.14, result.AsDouble());
    }

    [Fact]
    public void Read_InterpreterDump_BoolVector()
    {
        // 0 1 0 1 0 1 0 1 as APLBOOL
        byte[] dump = [
            0x00, 0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0xA4,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x55, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(ElType.APLBOOL, result.Zones.ElType);
        var ints = result.AsInt64Array();
        Assert.Equal([0, 1, 0, 1, 0, 1, 0, 1], ints);
    }

    [Fact]
    public void Read_InterpreterDump_Nested_42_hi()
    {
        // (42 'hi') as nested
        byte[] dump = [
            0x00, 0x00, 0x00, 0x58, 0x00, 0x00, 0x00, 0xA4,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x17, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x0F, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x1F, 0x27, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x68, 0x69, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(ZValueKind.Nested, result.Kind);
        Assert.Equal(2, result.Children.Length);
        Assert.Equal(42L, result.Children[0].AsInt64());
        Assert.Equal("hi", result.Children[1].AsString());
    }

    [Fact]
    public void Read_InterpreterDump_Matrix()
    {
        // 2 3⍴⍳6 as rank-2 APLSINT
        byte[] dump = [
            0x00, 0x00, 0x00, 0x30, 0x00, 0x00, 0x00, 0xA4,
            0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2F, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x00, 0x00
        ];

        var result = ZReader.Read(dump);
        Assert.Equal(2, result.Zones.Rank);
        Assert.Equal(2L, result.Shape[0]);
        Assert.Equal(3L, result.Shape[1]);

        var ints = result.AsInt64Array();
        Assert.Equal([1, 2, 3, 4, 5, 6], ints);
    }

    // ═══════════════════════════════════════════════════════════════
    // All sizes are multiples of 8
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("hello world")]
    [InlineData("café")]
    [InlineData("αβγδ")]
    public void AllBufferSizes_AreMultipleOf8(string s)
    {
        var buf = ZWriter.ToBytes(ZValue.FromString(s));
        Assert.Equal(0, buf.Length % 8);
    }

    [Fact]
    public void AllNumericBufferSizes_AreMultipleOf8()
    {
        var cases = new ZValue[]
        {
            ZValue.FromIntSqueezed(0),
            ZValue.FromIntSqueezed(1000),
            ZValue.FromIntSqueezed(100000),
            ZValue.FromDouble(1.5),
            ZValue.FromBooleans([true, false, true]),
            ZValue.FromBytes([1, 2, 3, 4, 5]),
            ZValue.FromInt32Array([1, 2, 3]),
        };

        foreach (var c in cases)
        {
            var buf = ZWriter.ToBytes(c);
            Assert.Equal(0, buf.Length % 8);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // DECF (Decimal128 BID) tests — from ⎕FR←1287 experiments
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Write_DecfInt64Scalar_MatchesInterpreter()
    {
        // 9007199254740993 (2^53+1) under ⎕FR←1287
        // Probe captured: eltype=14 (APLDECF_BID), size=40, wc=5, zones=0x2E0F
        // Data: 01 00 00 00 00 00 20 00 | 00 00 00 00 00 00 40 30
        var val = ZValue.FromDecfInt64(9007199254740993L);
        var buf = ZWriter.ToBytes(val);

        Assert.Equal(40, buf.Length);
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x2E0FL, zones); // TYPESIMPLE | rank=0 | eltype=14 | squoze

        // Data region starts at offset 24 (after header+wc+zones, no shape for scalar)
        byte[] expectedData = [0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20, 0x00,
                               0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x30];
        Assert.Equal(expectedData, buf[24..40]);
    }

    [Fact]
    public void Write_DecfMaxInt64_MatchesInterpreter()
    {
        // (2^63)-1 under ⎕FR←1287
        // Probe: FF FF FF FF FF FF FF 7F | 00 00 00 00 00 00 40 30
        var val = ZValue.FromDecfInt64(long.MaxValue);
        var buf = ZWriter.ToBytes(val);

        byte[] expectedData = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F,
                               0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x30];
        Assert.Equal(expectedData, buf[24..40]);
    }

    [Fact]
    public void RoundTrip_DecfInt64Scalar()
    {
        var original = ZValue.FromDecfInt64(9007199254740993L);
        var buf = ZWriter.ToBytes(original);
        var decoded = ZReader.Read(buf);

        Assert.Equal(ZValueKind.Scalar, decoded.Kind);
        Assert.Equal(ElType.APLDECF_BID, decoded.Zones.ElType);
        Assert.Equal(9007199254740993L, decoded.AsDecfInt64());
    }

    [Fact]
    public void RoundTrip_NegativeDecfInt64()
    {
        var original = ZValue.FromDecfInt64(-42);
        var buf = ZWriter.ToBytes(original);
        var decoded = ZReader.Read(buf);

        Assert.Equal(-42L, decoded.AsDecfInt64());
    }

    [Fact]
    public void Write_DecfVector_MatchesInterpreter()
    {
        // Vector of 3 DECF values: probe shows size=80, wc=10, zones=0x2E1F (rank=1,eltype=14)
        // Create 3 values with known BID128 representations
        var data = new byte[48]; // 3 × 16
        // Element 0: integer 11 → coefficient=11, high=0x3040000000000000
        data[0] = 0x0B; // low byte of coefficient
        BitConverter.GetBytes(0x3040000000000000L).CopyTo(data, 8);
        // Element 1: integer 22
        data[16] = 0x16;
        BitConverter.GetBytes(0x3040000000000000L).CopyTo(data, 24);
        // Element 2: integer 33
        data[32] = 0x21;
        BitConverter.GetBytes(0x3040000000000000L).CopyTo(data, 40);

        var val = ZValue.FromDecfArray(data, 3);
        var buf = ZWriter.ToBytes(val);

        Assert.Equal(80, buf.Length);
        long zones = MemoryMarshal.Read<long>(buf.AsSpan(16));
        Assert.Equal(0x2E1FL, zones); // rank=1, eltype=14
        long shape = MemoryMarshal.Read<long>(buf.AsSpan(24));
        Assert.Equal(3L, shape);
    }

    [Fact]
    public void RoundTrip_DecfVector()
    {
        var data = new byte[32]; // 2 × 16
        // Two DECF integers: 100 and 200
        data[0] = 100;
        BitConverter.GetBytes(0x3040000000000000L).CopyTo(data, 8);
        data[16] = 200;
        BitConverter.GetBytes(0x3040000000000000L).CopyTo(data, 24);

        var original = ZValue.FromDecfArray(data, 2);
        var buf = ZWriter.ToBytes(original);
        var decoded = ZReader.Read(buf);

        Assert.Equal(ZValueKind.DecfVector, decoded.Kind);
        Assert.Equal(2L, decoded.Shape[0]);
        Assert.Equal(data, decoded.AsDecfBytes());
    }

    // ═══════════════════════════════════════════════════════════════
    // Higher-rank array tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_BoolMatrix_2x8()
    {
        // 2 3⍴1 0 1 0 1 0 → 2×3 matrix packed flat
        var bools = new bool[] { true, false, true, false, true, false };
        var val = ZValue.FromBooleans([2, 3], bools);
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(2, decoded.Shape.Length); // rank 2
        Assert.Equal(2L, decoded.Shape[0]);
        Assert.Equal(3L, decoded.Shape[1]);
        // Verify bit layout: 101010 = 0xA8 (bits: 10101000)
        Assert.Equal(0xA8, decoded.RawData[0]);
    }

    [Fact]
    public void RoundTrip_BoolMatrix_3x5()
    {
        // 3×5 boolean matrix = 15 bits total, flat packed, no per-row padding
        var bools = new bool[15];
        for (int i = 0; i < 15; i++) bools[i] = (i % 2 == 0); // alternating 1 0 1 0...
        var val = ZValue.FromBooleans([3, 5], bools);
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(2, decoded.Shape.Length);
        Assert.Equal(3L, decoded.Shape[0]);
        Assert.Equal(5L, decoded.Shape[1]);
        var int64s = decoded.AsInt64Array();
        for (int i = 0; i < 15; i++)
            Assert.Equal(i % 2 == 0 ? 1L : 0L, int64s[i]);
    }

    [Fact]
    public void RoundTrip_IntMatrix()
    {
        // 2×3 int32 matrix
        var data = new int[] { 1, 2, 3, 4, 5, 6 };
        var val = ZValue.FromInt32Array([2, 3], data);
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(2, decoded.Shape.Length);
        Assert.Equal(2L, decoded.Shape[0]);
        Assert.Equal(3L, decoded.Shape[1]);
        var result = decoded.AsInt64Array();
        for (int i = 0; i < 6; i++)
            Assert.Equal(data[i], result[i]);
    }

    [Fact]
    public void RoundTrip_DoubleMatrix()
    {
        // 3×2 double matrix
        var data = new double[] { 1.1, 2.2, 3.3, 4.4, 5.5, 6.6 };
        var val = ZValue.FromDoubleArray([3, 2], data);
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(2, decoded.Shape.Length);
        Assert.Equal(3L, decoded.Shape[0]);
        Assert.Equal(2L, decoded.Shape[1]);
        var result = decoded.AsDoubleArray();
        for (int i = 0; i < 6; i++)
            Assert.Equal(data[i], result[i]);
    }

    [Fact]
    public void RoundTrip_CharMatrix()
    {
        // 2×3 character matrix (like a 2-row 3-col char grid)
        var val = ZValue.FromCharArray([2, 3], "ABCDEF");
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(2, decoded.Shape.Length);
        Assert.Equal(2L, decoded.Shape[0]);
        Assert.Equal(3L, decoded.Shape[1]);
        Assert.Equal("ABCDEF", decoded.AsString());
    }

    [Fact]
    public void RoundTrip_Rank3_IntArray()
    {
        // 2×3×4 int32 array (24 elements)
        var data = new int[24];
        for (int i = 0; i < 24; i++) data[i] = i * 10;
        var val = ZValue.FromInt32Array([2, 3, 4], data);
        var buf = ZWriter.ToBytes(val);
        var decoded = ZReader.Read(buf);

        Assert.Equal(3, decoded.Shape.Length);
        Assert.Equal(2L, decoded.Shape[0]);
        Assert.Equal(3L, decoded.Shape[1]);
        Assert.Equal(4L, decoded.Shape[2]);
        var result = decoded.AsInt64Array();
        for (int i = 0; i < 24; i++)
            Assert.Equal(data[i], result[i]);
    }

    [Fact]
    public void HigherRank_BooleanPadding_IsFlatNotPerRow()
    {
        // Key empirical finding: boolean arrays are packed FLAT (row-major bit stream)
        // with NO per-row or per-plane padding. Only final 8-byte alignment.
        // 3×3 = 9 bits → 2 bytes data → padded to 8 bytes in Z buffer
        var bools = new bool[9];
        bools[0] = true; bools[4] = true; bools[8] = true; // diagonal
        var val = ZValue.FromBooleans([3, 3], bools);
        var buf = ZWriter.ToBytes(val);

        // Expected bits: 1 0 0 0 1 0 0 0 1 (then 7 zeros padding to byte boundary)
        // = 0x88 0x80
        var decoded = ZReader.Read(buf);
        Assert.Equal(0x88, decoded.RawData[0]);
        Assert.Equal(0x80, decoded.RawData[1]);
    }

    [Fact]
    public void Write_HigherRank_BufferAlignment()
    {
        // All Z buffers must be 8-byte aligned in total length
        var shapes = new long[][] { [2, 3], [3, 5], [2, 3, 4], [4, 4] };
        foreach (var shape in shapes)
        {
            var total = 1L;
            foreach (var d in shape) total *= d;
            var bools = new bool[(int)total];
            var val = ZValue.FromBooleans(shape, bools);
            var buf = ZWriter.ToBytes(val);
            Assert.Equal(0, buf.Length % 8);
        }
    }
}
