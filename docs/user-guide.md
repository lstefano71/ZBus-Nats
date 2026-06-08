# ZFormat Library User Guide

Serialize and deserialize Dyalog APL arrays using the Z wire format — the binary protocol
used by `⎕NA` to exchange rich APL values with external DLLs.

## Overview

The `ZFormat` library provides three main types:

| Type | Purpose |
|------|---------|
| `ZValue` | Represents an APL array in memory. Create, inspect, and compose values. |
| `ZWriter` | Serializes a `ZValue` to Z wire format bytes or native memory. |
| `ZReader` | Deserializes Z wire format bytes or native pointers back to a `ZValue`. |
| `ZView` | Zero-copy `ref struct` for reading Z buffers without allocation (advanced). |

## Getting Started

Add a reference to the `ZFormat` project, then import the namespace:

```csharp
using ZFormat;
```

The library targets .NET 10+ and requires `AllowUnsafeBlocks` for native interop scenarios.

## Creating Values

### Scalar integers

`ZValue.FromInt` auto-selects the smallest APL storage type that fits the value:

```csharp
ZValue scalar = ZValue.FromInt(42);
// Stored as APLSINT (8-bit) because 42 fits in -128..127
```

For explicit control over storage width:

```csharp
ZValue i8  = ZValue.FromInt8((sbyte)42);     // APLSINT  — 1 byte
ZValue i16 = ZValue.FromInt16((short)1000);   // APLINTG  — 2 bytes
ZValue i32 = ZValue.FromInt32(100_000);       // APLLONG  — 4 bytes
ZValue i64 = ZValue.FromInt64(5_000_000_000); // APLQUAD  — 8 bytes
```

### Scalar doubles

```csharp
ZValue pi = ZValue.FromDouble(3.14159);
```

### Scalar booleans

```csharp
ZValue yes = ZValue.FromBool(true);
ZValue no  = ZValue.FromBool(false);
```

### Scalar characters

```csharp
ZValue ch = ZValue.FromChar('A');
ZValue rune = ZValue.FromRune(new Rune(0x1F600)); // 😀
```

### Character vectors (strings)

```csharp
ZValue greeting = ZValue.FromChars("hello");
// Stored as APLWCHAR8 (1 byte/char) because all codepoints ≤ 255

ZValue greek = ZValue.FromChars("αβγ");
// Stored as APLWCHAR16 (2 bytes/char) because codepoints > 255

ZValue emoji = ZValue.FromChars("Hi 🎉");
// Stored as APLWCHAR32 (4 bytes/char) because codepoints > 65535
```

> [!NOTE]
> The library automatically selects the narrowest character encoding that fits all codepoints
> in the string. Characters are stored as raw Unicode codepoints, not UTF-8.

### Integer vectors

```csharp
// Auto-squeezed to narrowest element width
ZValue small = ZValue.FromInts([1, 2, 3, 4, 5]);
// → APLSINT (all values fit in one byte)

ZValue large = ZValue.FromInts([1, 2, 3, 100_000]);
// → APLLONG (needs 32-bit to hold 100000)

// Explicit Int32 storage
ZValue explicit32 = ZValue.FromInt32Array([10, 20, 30]);
```

### Double vectors

```csharp
ZValue temps = ZValue.FromDoubles([36.5, 37.0, 38.2]);
```

### Boolean vectors

```csharp
ZValue mask = ZValue.FromBools([true, false, true, true]);
// Stored as APLBOOL — bit-packed, MSB-first
```

### Higher-rank arrays (matrices, etc.)

Every factory that creates vectors has an overload accepting an explicit shape:

```csharp
// 2×3 integer matrix  (⍴ is 2 3 in APL)
ZValue matrix = ZValue.FromInts(
    shape: [2, 3],
    data:  [1, 2, 3, 4, 5, 6]);

// 3×4 character matrix
ZValue charMatrix = ZValue.FromChars(
    shape: [3, 4],
    data:  "HelloWorldAPL!");

// 2×2×2 boolean cube
ZValue cube = ZValue.FromBools(
    shape: [2, 2, 2],
    data:  [true, false, true, false, false, true, false, true]);
```

### Nested arrays

Nested arrays correspond to APL's general (pointer-based) arrays, where each element
can be a different type or shape:

```csharp
// Equivalent to APL:  'hello' 42 (1.5 2.5 3.5)
ZValue nested = ZValue.Nested(
    ZValue.FromChars("hello"),
    ZValue.FromInt(42),
    ZValue.FromDoubles([1.5, 2.5, 3.5])
);

// Higher-rank nested: 2×2 matrix of mixed elements
ZValue nestedMatrix = ZValue.Nested(
    shape: [2, 2],
    items: [
        ZValue.FromChars("AB"),
        ZValue.FromInt(1),
        ZValue.FromInt(2),
        ZValue.FromChars("CD"),
    ]);
```

### Empty nested arrays with prototypes

In APL, empty nested arrays carry a **prototype** that determines the type of fill
elements. The Z format serializes this prototype as a phantom child:

```csharp
// 0⍴⊂'' — empty vector of strings (char prototype)
ZValue emptyStrings = ZValue.EmptyNested(ZValue.FromChars(""));

// 0⍴⊂0 — empty vector of numbers (int prototype)
ZValue emptyNums = ZValue.EmptyNested(ZValue.FromInt(0));

// Custom shape: 0 3 matrix (0 rows, 3 columns)
ZValue emptyMatrix = ZValue.EmptyNested(
    shape: [0, 3],
    prototype: ZValue.FromInt(0));
```

> [!WARNING]
> Serializing an empty nested array **without** a prototype will throw
> `InvalidOperationException` — the Dyalog interpreter crashes on such buffers.
> Always use `ZValue.EmptyNested(prototype)`.

### DECF (128-bit decimal float)

Dyalog uses 128-bit decimal floats (IEEE 754-2008 BID format) for values that exceed
64-bit integer range. There are no 64-bit integers in Dyalog APL — large integers are
exchanged as DECF:

```csharp
// Encode a large integer as DECF
ZValue bigInt = ZValue.FromDecfInt64(9_000_000_000_000_000_000);

// Raw 16-byte BID128 representation
byte[] bid128 = new byte[16];
// ... fill with BID128 encoding ...
ZValue decf = ZValue.FromDecf(bid128);
```

### Raw byte vectors

For opaque binary payloads, use `FromBytes` which stores data as `APLSINT`:

```csharp
ZValue payload = ZValue.FromBytes([0x48, 0x65, 0x6C, 0x6C, 0x6F]);
```

## Serialization with ZWriter

### Serialize to a byte array

Use `ZWriter.ToBytes` for testing, storage, or network transport:

```csharp
ZValue value = ZValue.FromChars("hello");
byte[] bytes = ZWriter.ToBytes(value);
// bytes contains the complete Z wire format buffer
```

### Serialize to native memory (⎕NA interop)

Use `ZWriter.WriteToNative` inside a `⎕NA` callback to return values to APL:

```csharp
// In a ⎕NA-exported function with signature:  result ← MyFn arg
// where result is declared as >Z or =Z
[UnmanagedCallersOnly(EntryPoint = "MyFn")]
public static unsafe void MyFn(nint zResult, nint zArg)
{
    ZValue result = ZValue.FromChars("Hello from .NET!");
    ZWriter.WriteToNative(zResult, result);
}
```

The native memory is allocated with `NativeMemory.AllocZeroed` and will be freed by
the Dyalog interpreter after it copies the data.

## Deserialization with ZReader

### Read from a byte array

```csharp
byte[] bytes = /* Z buffer from storage or network */;
ZValue value = ZReader.Read(bytes);
```

### Read from a ⎕NA input pointer

Use `ZReader.ReadFromNative` when receiving APL values via `⎕NA` `<Z` or `=Z` parameters:

```csharp
[UnmanagedCallersOnly(EntryPoint = "ProcessData")]
public static unsafe void ProcessData(nint zResult, nint zInput)
{
    ZValue input = ZReader.ReadFromNative(zInput);
    // Process the APL value...
}
```

### Read from a raw buffer pointer

Use `ZReader.ReadDirect` when you already have the buffer address (no self-pointer
indirection):

```csharp
ZValue value = ZReader.ReadDirect(bufferPtr);
```

## Inspecting Values

### Type discrimination

```csharp
ZValue value = ZReader.Read(bytes);

switch (value.Type)
{
    case ZType.Char:
        Console.WriteLine($"String: {value.AsString()}");
        break;
    case ZType.Int:
        Console.WriteLine($"Integer: {value.AsInt64()}");
        break;
    case ZType.Double:
        Console.WriteLine($"Double: {value.AsDouble()}");
        break;
    case ZType.Bool:
        Console.WriteLine($"Boolean array");
        break;
    case ZType.Nested:
        Console.WriteLine($"Nested with {value.ElementCount} elements");
        break;
    case ZType.Decf:
        Console.WriteLine($"Decimal float");
        break;
}
```

### Shape and rank

```csharp
ZValue matrix = ZReader.Read(bytes);

int rank = matrix.Shape.Length;        // 0=scalar, 1=vector, 2=matrix, ...
long rows = matrix.Shape[0];           // first dimension
long cols = matrix.Shape[1];           // second dimension
long total = matrix.ElementCount;      // product of all dimensions
```

### Reading typed data

```csharp
// Strings
string s = value.AsString();

// Scalar integer
long n = value.AsInt64();

// Integer vector/matrix as long[]
long[] ints = value.AsInt64Array();

// Scalar double
double d = value.AsDouble();

// Double vector/matrix as double[]
double[] doubles = value.AsDoubleArray();

// DECF as 64-bit integer (when you know it's an exact integer)
long bigNum = value.AsDecfInt64();
```

### Zero-copy span access

For performance-critical paths, avoid allocation with typed span accessors:

```csharp
// Only valid when ElType exactly matches — no conversion
ReadOnlySpan<int> ints = value.SpanInt32();       // APLLONG only
ReadOnlySpan<double> dbls = value.SpanDouble();   // APLDOUB only
ReadOnlySpan<short> shorts = value.SpanInt16();   // APLINTG only
ReadOnlySpan<long> longs = value.SpanInt64();     // APLQUAD only
```

### Navigating nested arrays

```csharp
ZValue nested = ZReader.Read(bytes);

// Access children by index
ZValue first = nested[0];
ZValue second = nested[1];

// Iterate all children
foreach (var child in nested.Children)
{
    Console.WriteLine($"  Type={child.Type}, Elements={child.ElementCount}");
}

// Check for empty nested with prototype
if (nested.ElementCount == 0 && nested.Prototype != null)
{
    Console.WriteLine($"Empty nested, prototype type: {nested.Prototype.Type}");
}
```

## Zero-Copy Reading with ZView

`ZView` is a `ref struct` that provides zero-copy access to Z buffers. It never allocates
managed memory for data — all reads are direct spans over the original buffer. The compiler
enforces that a `ZView` cannot escape the stack frame, making it safe for use during
`⎕NA` callbacks where the buffer is only valid for the duration of the call.

```csharp
[UnmanagedCallersOnly(EntryPoint = "FastProcess")]
public static unsafe void FastProcess(nint zResult, nint zInput)
{
    var view = ZView.FromNative(zInput);

    if (view.Type == ZType.Int)
    {
        // Zero-copy span access — no allocation
        ReadOnlySpan<int> data = view.SpanInt32();
        int sum = 0;
        foreach (int x in data) sum += x;

        ZWriter.WriteToNative(zResult, ZValue.FromInt32(sum));
    }
}
```

### ZView vs ZValue

| | `ZValue` | `ZView` |
|---|----------|---------|
| Memory | Copies data to managed heap | Zero-copy spans over native buffer |
| Lifetime | Unlimited — use anywhere | Must not outlive the ⎕NA call |
| Type | `class` — heap allocated | `ref struct` — stack only |
| Nested navigation | `.Children` span, indexer | Lazy indexer (scans siblings) |
| Use case | Storage, return values, building | Hot-path read-only processing |

### Converting ZView to ZValue

If you need to persist data beyond the callback, call `ToZValue()` to copy:

```csharp
var view = ZView.FromNative(zInput);
ZValue persisted = view.ToZValue(); // copies to managed memory
// 'persisted' is safe to store, return, or use after the ⎕NA call returns
```

### ZView from managed spans (testing)

```csharp
byte[] bytes = ZWriter.ToBytes(ZValue.FromInt32Array([1, 2, 3]));
var view = ZView.FromSpan(bytes);
ReadOnlySpan<int> data = view.SpanInt32();
```

## Complete Example: Echo DLL

A minimal `⎕NA` DLL that reads an APL argument and echoes it back:

```csharp
using System.Runtime.InteropServices;
using ZFormat;

public static class EchoDll
{
    /// <summary>
    /// APL declaration:  echo ← 'echo.dll|Echo' =Z =Z
    /// Usage:           echo 'Hello'    ⍝ → 'Hello'
    ///                  echo 1 2 3      ⍝ → 1 2 3
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Echo")]
    public static unsafe void Echo(nint zResult, nint zInput)
    {
        ZValue input = ZReader.ReadFromNative(zInput);
        ZWriter.WriteToNative(zResult, input);
    }
}
```

## Complete Example: String Processing

```csharp
using System.Runtime.InteropServices;
using ZFormat;

public static class StringDll
{
    /// <summary>
    /// APL declaration:  upper ← 'strings.dll|ToUpper' =Z =Z
    /// Usage:           upper 'hello world'   ⍝ → 'HELLO WORLD'
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "ToUpper")]
    public static unsafe void ToUpper(nint zResult, nint zInput)
    {
        ZValue input = ZReader.ReadFromNative(zInput);
        string text = input.AsString();
        string upper = text.ToUpperInvariant();
        ZWriter.WriteToNative(zResult, ZValue.FromChars(upper));
    }
}
```

## Complete Example: Multi-Argument Function

In `⎕NA`, multiple arguments arrive as elements of a nested array:

```csharp
using System.Runtime.InteropServices;
using ZFormat;

public static class MathDll
{
    /// <summary>
    /// APL declaration:  add ← 'math.dll|Add' =Z <Z <Z
    /// Usage:           add 3 4      ⍝ → 7
    ///
    /// Note: each ⎕NA argument is one element. If the function takes one
    /// argument, it arrives as a scalar. Enclose character arrays:  fn (⊂'text')
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Add")]
    public static unsafe void Add(nint zResult, nint zLeft, nint zRight)
    {
        ZValue left = ZReader.ReadFromNative(zLeft);
        ZValue right = ZReader.ReadFromNative(zRight);

        long a = left.AsInt64();
        long b = right.AsInt64();

        ZWriter.WriteToNative(zResult, ZValue.FromInt(a + b));
    }
}
```

## Complete Example: Returning Nested Results

```csharp
/// <summary>
/// APL declaration:  info ← 'info.dll|GetInfo' =Z =Z
/// Returns:  ('name' value) pairs as a nested vector
/// </summary>
[UnmanagedCallersOnly(EntryPoint = "GetInfo")]
public static unsafe void GetInfo(nint zResult, nint zInput)
{
    var pairs = ZValue.Nested(
        ZValue.Nested(
            ZValue.FromChars("os"),
            ZValue.FromChars(Environment.OSVersion.ToString())
        ),
        ZValue.Nested(
            ZValue.FromChars("cores"),
            ZValue.FromInt(Environment.ProcessorCount)
        ),
        ZValue.Nested(
            ZValue.FromChars("time"),
            ZValue.FromChars(DateTime.UtcNow.ToString("O"))
        )
    );

    ZWriter.WriteToNative(zResult, pairs);
}
```

## Element Types Reference

The `ElType` enum represents the exact wire storage type:

| ElType | Description | Bytes/Element | .NET Read Type |
|--------|-------------|:---:|----------------|
| `APLBOOL` | Bit-packed booleans (MSB-first) | ⅛ | `AsInt64Array()` |
| `APLSINT` | Signed 8-bit integer | 1 | `AsInt64Array()` |
| `APLINTG` | Signed 16-bit integer | 2 | `SpanInt16()` |
| `APLLONG` | Signed 32-bit integer | 4 | `SpanInt32()` |
| `APLQUAD` | Signed 64-bit integer | 8 | `SpanInt64()` |
| `APLDOUB` | IEEE 754 double | 8 | `SpanDouble()` |
| `APLWCHAR8` | Unicode char (0–255) | 1 | `AsString()` |
| `APLWCHAR16` | Unicode char (0–65535) | 2 | `AsString()` |
| `APLWCHAR32` | Full Unicode char | 4 | `AsString()` |
| `APLDECF_BID` | Decimal float (BID128) | 16 | `AsDecfInt64()` |
| `APLPNTR` | Nested pointer slot | 8 | `Children` |

The coarse `ZType` enum groups these for pattern matching:

| ZType | Covers |
|-------|--------|
| `Bool` | `APLBOOL` |
| `Byte` | `APLSINT` (raw byte data) |
| `Char` | `APLWCHAR8`, `APLWCHAR16`, `APLWCHAR32`, `APLNCHAR` |
| `Int` | `APLINTG`, `APLLONG`, `APLQUAD` |
| `Double` | `APLDOUB` |
| `Decf` | `APLDECF_BID`, `APLDECF_DPD` |
| `Nested` | `APLPNTR` |

## Thread Safety

`ZWriter.ToBytes` and `ZReader.Read` are stateless static methods — they are safe to
call concurrently from multiple threads. `ZValue` instances are immutable once created.

## Error Handling

| Exception | Cause |
|-----------|-------|
| `InvalidOperationException` | Type mismatch (e.g., calling `AsString()` on an integer value) |
| `InvalidOperationException` | Serializing empty nested array without prototype |
| `ArgumentException` | Buffer too small or malformed Z header |
| `ArgumentOutOfRangeException` | Nested child index out of bounds |

## See Also

- [Z Wire Format Specification](z-wire-format.md) — full binary format details
- `samples/ZProbeManaged/` — AOT-compiled integration test DLL
- `tests/ZFormat.Tests/` — unit tests with golden bytes from the Dyalog 20.0 interpreter
