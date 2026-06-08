using System.Runtime.InteropServices;
using Xunit;
using ZFormat;

namespace ZFormat.Tests;

public class ZViewTests
{
    // Golden bytes from interpreter dumps (same as ZRoundTripTests)
    private static readonly byte[] Int42 = [
        0x00, 0x00, 0x00, 0x20, 0x00, 0x00, 0x00, 0xA4,
        0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0F, 0x22, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x2A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static readonly byte[] Nested42Hi = [
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

    private static readonly byte[] EmptyNestedChar = [
        0x00, 0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0xA4,
        0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x17, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x1F, 0x27, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    [Fact]
    public void Scalar_Int_Properties()
    {
        var view = ZView.FromSpan(Int42);
        Assert.Equal(ZType.Byte, view.Type); // APLSINT → Byte
        Assert.Equal(ElType.APLSINT, view.ElType);
        Assert.Equal(0, view.Rank);
        Assert.Equal(1L, view.ElementCount);
        Assert.False(view.IsEmpty);
    }

    [Fact]
    public void Scalar_Int_AsInt64()
    {
        var view = ZView.FromSpan(Int42);
        Assert.Equal(42L, view.AsInt64());
    }

    [Fact]
    public void Nested_Properties()
    {
        var view = ZView.FromSpan(Nested42Hi);
        Assert.Equal(ZType.Nested, view.Type);
        Assert.Equal(1, view.Rank);
        Assert.Equal(2L, view.Shape(0));
        Assert.Equal(2L, view.ElementCount);
        Assert.False(view.IsEmpty);
    }

    [Fact]
    public void Nested_Child_Navigation()
    {
        var view = ZView.FromSpan(Nested42Hi);

        var child0 = view[0];
        Assert.Equal(ZType.Byte, child0.Type);
        Assert.Equal(42L, child0.AsInt64());

        var child1 = view[1];
        Assert.Equal(ZType.Char, child1.Type);
        Assert.Equal("hi", child1.AsString());
    }

    [Fact]
    public void EmptyNested_Prototype()
    {
        var view = ZView.FromSpan(EmptyNestedChar);
        Assert.Equal(ZType.Nested, view.Type);
        Assert.Equal(0L, view.ElementCount);
        Assert.True(view.IsEmpty);
        Assert.True(view.HasPrototype);

        var proto = view.Prototype;
        Assert.Equal(ZType.Char, proto.Type);
        Assert.Equal(0L, proto.ElementCount);
    }

    [Fact]
    public void SpanInt32_ZeroCopy()
    {
        // Create a Z buffer with an int32 vector [1, 2, 3]
        var value = ZValue.FromInt32Array([1, 2, 3]);
        var buf = ZWriter.ToBytes(value);

        var view = ZView.FromSpan(buf);
        var span = view.SpanInt32();
        Assert.Equal(3, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal(2, span[1]);
        Assert.Equal(3, span[2]);
    }

    [Fact]
    public void SpanDouble_ZeroCopy()
    {
        var value = ZValue.FromDoubles([1.5, 2.5, 3.5]);
        var buf = ZWriter.ToBytes(value);

        var view = ZView.FromSpan(buf);
        var span = view.SpanDouble();
        Assert.Equal(3, span.Length);
        Assert.Equal(1.5, span[0]);
        Assert.Equal(2.5, span[1]);
        Assert.Equal(3.5, span[2]);
    }

    [Fact]
    public void AsString_FromView()
    {
        var value = ZValue.FromChars("café");
        var buf = ZWriter.ToBytes(value);

        var view = ZView.FromSpan(buf);
        Assert.Equal("café", view.AsString());
    }

    [Fact]
    public void ToZValue_RoundTrip()
    {
        var view = ZView.FromSpan(Nested42Hi);
        var zval = view.ToZValue();

        Assert.Equal(ZType.Nested, zval.Type);
        Assert.Equal(2, zval.Children.Length);
        Assert.Equal(42L, zval[0].AsInt64());
        Assert.Equal("hi", zval[1].AsString());
    }

    [Fact]
    public void ToZValue_EmptyNested_PreservesPrototype()
    {
        var view = ZView.FromSpan(EmptyNestedChar);
        var zval = view.ToZValue();

        Assert.Equal(ZType.Nested, zval.Type);
        Assert.Equal(0L, zval.ElementCount);
        Assert.NotNull(zval.Prototype);
        Assert.Equal(ZType.Char, zval.Prototype!.Type);
    }

    [Fact]
    public void CopyShapeTo_Works()
    {
        // 2×3 int matrix
        var value = ZValue.FromInt32Array([2, 3], [1, 2, 3, 4, 5, 6]);
        var buf = ZWriter.ToBytes(value);

        var view = ZView.FromSpan(buf);
        var shape = new long[2];
        view.CopyShapeTo(shape);
        Assert.Equal(2L, shape[0]);
        Assert.Equal(3L, shape[1]);
    }

    [Fact]
    public void BoolVector_RawData()
    {
        var value = ZValue.FromBools([true, false, true, true, false, false, true, false]);
        var buf = ZWriter.ToBytes(value);

        var view = ZView.FromSpan(buf);
        Assert.Equal(ZType.Bool, view.Type);
        Assert.Equal(8L, view.ElementCount);
        // Bool data is bit-packed — 1 byte for 8 bools
        Assert.Equal(1, view.RawData.Length);
    }

    [Fact]
    public void DeepNested_Navigation()
    {
        // ((1 'A')(2 (3 'B')))
        var deep = ZValue.Nested(
            ZValue.Nested(ZValue.FromInt(1), ZValue.FromChars("A")),
            ZValue.Nested(ZValue.FromInt(2), ZValue.Nested(ZValue.FromInt(3), ZValue.FromChars("B"))));
        var buf = ZWriter.ToBytes(deep);

        var view = ZView.FromSpan(buf);
        Assert.Equal(ZType.Nested, view.Type);

        var c0 = view[0]; // (1 'A')
        Assert.Equal(ZType.Nested, c0.Type);
        Assert.Equal(1L, c0[0].AsInt64());
        Assert.Equal("A", c0[1].AsString());

        var c1 = view[1]; // (2 (3 'B'))
        Assert.Equal(2L, c1[0].AsInt64());
        var c1_1 = c1[1]; // (3 'B')
        Assert.Equal(3L, c1_1[0].AsInt64());
        Assert.Equal("B", c1_1[1].AsString());
    }
}
