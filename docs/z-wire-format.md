# Z Wire Format Specification

The Z wire format is Dyalog APL's serialisation format for passing arbitrary APL arrays across process and DLL boundaries via `⎕NA`. It produces a flat, pointerless byte buffer that faithfully represents any APL array — scalars, vectors, matrices, and nested structures of any depth.

## Key Properties

- **Self-describing**: every buffer encodes its own type, rank, shape, and data
- **Pointerless**: nested arrays are serialised inline — no pointer fixup needed
- **Native-endian payload**: all fields after the 8-byte header use platform byte order (little-endian on x64)
- **8-byte aligned**: every logical section is padded to an 8-byte boundary
- **Squeeze-preserving**: the interpreter serialises integers and characters using the smallest type that fits the data

## `⎕NA` Type Specifiers

| Specifier | Direction | Description |
|-----------|-----------|-------------|
| `<Z` | In (APL → DLL) | Interpreter serialises the APL argument and passes a pointer to the Z buffer |
| `>Z` | Out (DLL → APL) | DLL allocates a Z buffer; interpreter deserialises it into an APL array |
| `=Z` | In/Out | Combines both: input arrives serialised, output via pointer redirect |

Unlike PP (LOCALP) parameters, Z parameters are compatible with the `&` (async) modifier in `⎕NA`.

---

## Buffer Layout

Every Z buffer begins with an 8-byte big-endian header, followed by a native-endian pocket payload:

```
Offset  Size   Endian   Field
──────  ─────  ──────   ─────
0       4      big      total_size    — total buffer length in bytes (header + payload)
4       4      big      flags         — architecture/version byte (0xA4 on 64-bit Unicode)
8       8      little   wc            — word count (in-memory pocket Length)
16      8      little   zones         — type metadata bitfield
24      R×8    little   shape[]       — one int64 per dimension (absent for rank-0 scalars)
24+R×8  varies little   data          — element data, zero-padded to 8-byte boundary
```

### Extended Header (from Morten Kromberg's description)

The 8-byte header encodes:

- **Bytes 0–3** (big-endian): the 32 least significant bits of the overall length
- **Bytes 4–7** (big-endian): the next 21 bits of length in the top 21 bits of this word (supporting up to 2⁵³ byte buffers), plus an 8-bit architecture byte in the low 8 bits

On current 64-bit Unicode interpreters, the flags word is always `0x000000A4` because buffers don't exceed 4 GB and the architecture byte is `0xA4`.

---

## Word Count (wc)

The `wc` field equals the in-memory pocket `Header.Length` — the same value the interpreter stores in the PocketHeader. It represents the pocket's total size in 8-byte words, **including** the 16-byte PocketHeader (Length + RefCount), even though RefCount is not serialised:

```
wc = (PocketHeader(16) + zones(8) + rank×8 + data_padded) / 8
```

For simple arrays: `total_size = wc × 8` (the Z header's 8 bytes exactly replace the 8 bytes of RefCount that are omitted).

For nested arrays: `wc` describes only the **root** pocket's virtual size. The total buffer is larger.

---

## Zones Bitfield

The 64-bit zones field encodes pocket type metadata in bit slices:

```
Bits    Width  Field
──────  ─────  ─────
[0:4]   4      pocket type     (0xF=TYPESIMPLE, 0x7=TYPEGEN)
[4:8]   4      rank            (0–15)
[8:12]  4      element type    (from ELTYPES enum)
[12]    1      sticky flag
[13]    1      squoze flag     (set for simple squeezed arrays)
[14]    1      mmflag
[15]    1      mmflag2
[16]    1      mapped
[17:19] 2      mmpad
[19]    1      old_sortup
[20]    1      old_sortdn
```

In practice, only bits 0–13 matter for Z serialisation. The squoze flag (bit 13) is always set for simple arrays and always clear for nested (TYPEGEN).

### Formula

```
zones = (type & 0xF) | ((rank & 0xF) << 4) | ((eltype & 0xF) << 8) | (squoze << 13)
```

---

## Element Types

| Value | Name | Size | Description |
|-------|------|------|-------------|
| 0 | APLNCHAR | 1 byte | Classic (pre-Unicode) narrow character |
| 1 | APLBOOL | 1 bit | Boolean (bit-packed, MSB-first) |
| 2 | APLSINT | 1 byte | Signed 8-bit integer (−128 to 127, unsigned 0–255) |
| 3 | APLINTG | 2 bytes | Signed 16-bit integer |
| 4 | APLLONG | 4 bytes | Signed 32-bit integer |
| 5 | APLDOUB | 8 bytes | IEEE 754 64-bit double |
| 6 | APLPNTR | 8 bytes | Nested element (pointer slot, used in TYPEGEN root) |
| 7 | APLWCHAR8 | 1 byte | Unicode character squeezed to 8 bits (codepoints 0–255) |
| 8 | APLWCHAR16 | 2 bytes | Unicode character squeezed to 16 bits (codepoints 0–65535) |
| 9 | APLWCHAR32 | 4 bytes | Full Unicode character (codepoints 0–1114111) |
| 10 | APLCMPX | 16 bytes | Complex number (two doubles) |
| 11 | APLRATS | 16 bytes | Rational number |
| 12 | APLDECF_DPD | 16 bytes | Decimal float (DPD encoding) |
| 13 | APLQUAD | 8 bytes | 64-bit integer |
| 14 | APLDECF_BID | 16 bytes | Decimal float (BID encoding) |

### Character Type Clarification

**APLWCHAR8 is NOT UTF-8.** Each byte stores a raw Unicode codepoint (0–255). The character `'é'` (U+00E9) is stored as the single byte `0xE9`, not as the UTF-8 two-byte sequence `0xC3 0xA9`.

The interpreter **squeezes** character arrays to the narrowest width that fits all codepoints:
- All codepoints ≤ 255 → APLWCHAR8 (1 byte/char)
- Any codepoint > 255 but ≤ 65535 → APLWCHAR16 (2 bytes/char, little-endian)
- Any codepoint > 65535 → APLWCHAR32 (4 bytes/char, little-endian)

UTF-8 encoding/decoding is handled explicitly — either in APL via `'UTF-8' ⎕UCS`, or at the `⎕NA` boundary via the `UTF8` type (e.g., `<0UTF8[]`). The `T1` type does NOT perform any encoding — it simply passes raw byte values. The Z format itself always uses raw codepoints at the squeezed width.

### Integer Squeezing

The interpreter chooses the smallest integer type that fits the range of values in the array:
- Values 0–127 (or −128 to 127) → APLSINT (1 byte)
- Values requiring 16 bits → APLINTG (2 bytes)
- Values requiring 32 bits → APLLONG (4 bytes)
- Values requiring 64 bits → APLQUAD (8 bytes)

The interpreter accepts **any** integer type for deserialization — writing all integers as APLLONG (4 bytes) is always valid, just less space-efficient.

### Boolean Bit Packing

Boolean arrays (APLBOOL, eltype=1) are **bit-packed, MSB-first**:
- Element 0 occupies the most significant bit (bit 7) of byte 0
- Element 1 occupies bit 6 of byte 0
- Element 7 occupies bit 0 of byte 0
- Element 8 occupies bit 7 of byte 1
- etc.

The data region is padded to 8 bytes regardless of element count. The `shape` field gives the element count in bits, not bytes.

**Proven examples:**
- `0 1 0 1 0 1 0 1` → byte `0x55` (binary: 01010101)
- `1 1 1 1 0 0 0 0` → byte `0xF0` (binary: 11110000)
- `1 0` (2 elements) → byte `0x80` (binary: 10000000, only top 2 bits meaningful)

#### Higher-Rank Boolean Arrays

**Empirically verified**: Higher-rank boolean arrays use **flat row-major bit packing** with **no per-row or per-plane padding**. The bits flow continuously across all dimensions. The only padding is the final 8-byte alignment of the entire data region.

Example: a `3×3` boolean identity matrix `(3 3⍴1 0 0 0 1 0 0 0 1)` is packed as:
- Bits: `1 0 0 | 0 1 0 | 0 0 1` (9 bits, row-major, no row boundary padding)
- Bytes: `0x88 0x80` (= `10001000 10000000`, only 9 MSB bits meaningful)
- Padded to 8 bytes in the Z buffer

This means to address bit `[i,j]` in a `rows×cols` matrix, the linear bit index is `i*cols + j`.

### Decimal Floating Point (DECF / Decimal128)

When `⎕FR←1287`, the interpreter stores numeric values as IEEE 754-2008 decimal128. This is commonly used to represent **exact 64-bit integers** that cannot be expressed precisely in binary floating point (double has only 53 bits of mantissa).

| Platform | Element Type | Encoding |
|----------|--------------|----------|
| Windows, Linux | `APLDECF_BID` (14) | Binary Integer Decimal |
| AIX | `APLDECF_DPD` (12) | Densely Packed Decimal |

Each DECF element is **16 bytes** (128 bits), stored in native byte order:
- Bytes 0–7: low 64 bits of the coefficient (little-endian on x64)
- Bytes 8–15: combination field (exponent + high bits of coefficient + sign)

For integer values with exponent=0:
- Coefficient = the integer magnitude
- High word = `0x3040000000000000` (positive) or `0xB040000000000000` (negative)
- The exponent bias is 6176; exponent=0 → biased=6176=0x1820, placed in bits 49–62 of the high word

**Proven examples** (from probe, ⎕FR←1287):
- Integer `9007199254740993` (2^53+1): `01 00 00 00 00 00 20 00 | 00 00 00 00 00 00 40 30`
- Integer `9223372036854775807` (2^63−1): `FF FF FF FF FF FF FF 7F | 00 00 00 00 00 00 40 30`
- Value `3.14`: `3A 01 00 00 00 00 00 00 | 00 00 00 00 00 00 3C 30` (coefficient=314, exponent=−2)

---

## Proven Zones Values

Observed from the Dyalog 20.0 interpreter via probe DLL:

| Zones | Meaning |
|-------|---------|
| `0x211F` | TYPESIMPLE \| rank1 \| APLBOOL \| squoze — boolean vector |
| `0x220F` | TYPESIMPLE \| rank0 \| APLSINT \| squoze — byte scalar |
| `0x221F` | TYPESIMPLE \| rank1 \| APLSINT \| squoze — byte vector |
| `0x222F` | TYPESIMPLE \| rank2 \| APLSINT \| squoze — byte matrix |
| `0x230F` | TYPESIMPLE \| rank0 \| APLINTG \| squoze — int16 scalar |
| `0x231F` | TYPESIMPLE \| rank1 \| APLINTG \| squoze — int16 vector |
| `0x240F` | TYPESIMPLE \| rank0 \| APLLONG \| squoze — int32 scalar |
| `0x250F` | TYPESIMPLE \| rank0 \| APLDOUB \| squoze — double scalar |
| `0x271F` | TYPESIMPLE \| rank1 \| APLWCHAR8 \| squoze — char vector (1-byte codepoints) |
| `0x281F` | TYPESIMPLE \| rank1 \| APLWCHAR16 \| squoze — char vector (2-byte codepoints) |
| `0x290F` | TYPESIMPLE \| rank0 \| APLWCHAR32 \| squoze — char scalar (4-byte codepoint) |
| `0x2E0F` | TYPESIMPLE \| rank0 \| APLDECF_BID \| squoze — DECF scalar |
| `0x2E1F` | TYPESIMPLE \| rank1 \| APLDECF_BID \| squoze — DECF vector |
| `0x0617` | TYPEGEN \| rank1 \| APLPNTR — nested vector (no squoze) |

---

## Simple Array Examples

### Character vector `'hello'` — APLWCHAR8

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 27 00 00 00 00 00 00  05 00 00 00 00 00 00 00
00000020  68 65 6C 6C 6F 00 00 00
```
- size=40, flags=0xA4, wc=5, zones=0x271F, shape=5, data=`hello` + 3 pad bytes

### Character vector `'café'` — APLWCHAR8 (codepoints ≤ 255)

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 27 00 00 00 00 00 00  04 00 00 00 00 00 00 00
00000020  63 61 66 E9 00 00 00 00
```
- `'é'` = U+00E9 stored as byte `0xE9` (NOT UTF-8)

### Character vector `'αβγ'` — APLWCHAR16 (codepoints > 255)

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 28 00 00 00 00 00 00  03 00 00 00 00 00 00 00
00000020  B1 03 B2 03 B3 03 00 00
```
- α=U+03B1 stored as `B1 03` (little-endian 16-bit)

### Character scalar `'😀'` — APLWCHAR32 (codepoint > 65535)

```
00000000  00 00 00 20 00 00 00 A4  04 00 00 00 00 00 00 00
00000010  0F 29 00 00 00 00 00 00  00 F6 01 00 00 00 00 00
```
- rank=0 (scalar!), 😀=U+1F600 stored as `00 F6 01 00` (little-endian 32-bit)
- Note: single characters are scalars, not 1-element vectors

### Integer scalar `42` — APLSINT (fits in 1 byte)

```
00000000  00 00 00 20 00 00 00 A4  04 00 00 00 00 00 00 00
00000010  0F 22 00 00 00 00 00 00  2A 00 00 00 00 00 00 00
```

### Integer scalar `1000` — APLINTG (requires 16 bits)

```
00000000  00 00 00 20 00 00 00 A4  04 00 00 00 00 00 00 00
00000010  0F 23 00 00 00 00 00 00  E8 03 00 00 00 00 00 00
```
- 1000 = 0x03E8, stored as `E8 03` (little-endian) in an 8-byte data slot

### Integer scalar `100000` — APLLONG (requires 32 bits)

```
00000000  00 00 00 20 00 00 00 A4  04 00 00 00 00 00 00 00
00000010  0F 24 00 00 00 00 00 00  A0 86 01 00 00 00 00 00
```

### Float scalar `3.14` — APLDOUB

```
00000000  00 00 00 20 00 00 00 A4  04 00 00 00 00 00 00 00
00000010  0F 25 00 00 00 00 00 00  1F 85 EB 51 B8 1E 09 40
```
- IEEE 754 double: `0x400921FB54442D18` → wait, let's check: 3.14 = `0x40091EB851EB851F` stored LE as `1F 85 EB 51 B8 1E 09 40` ✓

### Boolean vector `0 1 0 1 0 1 0 1` — APLBOOL

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 21 00 00 00 00 00 00  08 00 00 00 00 00 00 00
00000020  55 00 00 00 00 00 00 00
```
- shape=8 (8 boolean elements), data=0x55 (MSB-first bit-packing)

### Integer vector `10 20 30` — APLSINT (all fit in 1 byte)

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 22 00 00 00 00 00 00  03 00 00 00 00 00 00 00
00000020  0A 14 1E 00 00 00 00 00
```

### Int16 vector `256 257 258` — APLINTG

```
00000000  00 00 00 28 00 00 00 A4  05 00 00 00 00 00 00 00
00000010  1F 23 00 00 00 00 00 00  03 00 00 00 00 00 00 00
00000020  00 01 01 01 02 01 00 00
```
- 256=`00 01`, 257=`01 01`, 258=`02 01` (little-endian 16-bit)

### Byte matrix `2 3⍴⍳6` — rank-2 APLSINT

```
00000000  00 00 00 30 00 00 00 A4  06 00 00 00 00 00 00 00
00000010  2F 22 00 00 00 00 00 00  02 00 00 00 00 00 00 00
00000020  03 00 00 00 00 00 00 00  01 02 03 04 05 06 00 00
```
- zones=0x222F (rank=2), shape = [2, 3], data = row-major: 1,2,3,4,5,6

---

## Nested Arrays

Nested arrays use pocket type TYPEGEN (0x7) with element type APLPNTR (0x6). Children are serialised **inline** after the root's shape — no pointer table, no offsets.

### Layout

```
[Z header: 8 bytes]
[Root: wc | zones | shape[N]]              ← root describes N pointer slots
[Child 0: wc | zones | [shape] | data]    ← each child is self-contained
[Child 1: wc | zones | [shape] | data]
...
```

### Root wc

The root's wc is the **virtual** in-memory pocket Length — as if the pocket contained N actual 8-byte pointer slots:

```
root_wc = (PocketHeader(16) + zones(8) + rank×8 + N×pointer_slot(8)) / 8
```

For a rank-1 nested vector with N elements: `root_wc = (16 + 8 + 8 + N×8) / 8 = (32 + N×8) / 8`

The root wc does NOT account for the children's serialised bytes. The total buffer size exceeds `root_wc × 8`.

### Child format

Each child follows the same pocket format as a standalone simple array:
```
child_wc = (PocketHeader(16) + zones(8) + child_rank×8 + data_padded) / 8
```

### Deeply nested arrays

Children can themselves be TYPEGEN pockets with their own inline children. The format is recursive.

### Example: `42 'hi'` — 88 bytes

```
Offset  Qword   Meaning
──────  ─────   ───────
[0]     Z hdr   size=88 (BE), flags=0xA4 (BE)
[8]     6       root wc = (16+8+8+2×8)/8 = 6
[16]    0x0617  root zones: TYPEGEN|rank1|APLPNTR (no squoze)
[24]    2       root shape[0]: 2 elements
[32]    4       child 0 wc (APLSINT scalar)
[40]    0x220F  child 0 zones: TYPESIMPLE|rank0|APLSINT|squoze
[48]    42      child 0 data
[56]    5       child 1 wc (char vector "hi")
[64]    0x271F  child 1 zones: TYPESIMPLE|rank1|APLWCHAR8|squoze
[72]    2       child 1 shape[0]: 2 chars
[80]    "hi"    child 1 data: 0x68 0x69 + 6 pad bytes
```

Hex dump:
```
00000000  00 00 00 58 00 00 00 A4  06 00 00 00 00 00 00 00
00000010  17 06 00 00 00 00 00 00  02 00 00 00 00 00 00 00
00000020  04 00 00 00 00 00 00 00  0F 22 00 00 00 00 00 00
00000030  2A 00 00 00 00 00 00 00  05 00 00 00 00 00 00 00
00000040  1F 27 00 00 00 00 00 00  02 00 00 00 00 00 00 00
00000050  68 69 00 00 00 00 00 00
```

---

## Calling Convention Details

### `<Z` — Input (interpreter → DLL)

The interpreter serialises the APL value and passes a pointer to: `[8-byte self-pointer][Z payload]`.

```
z_param → ┌────────────────────┐
           │ self-pointer (8B)  │ ← points to Z payload below
           ├────────────────────┤
           │ Z header (8B)      │ ← start of Z buffer
           │ wc, zones, ...     │
           │ data...            │
           └────────────────────┘
```

The DLL reads the Z buffer starting at `*(intptr_t*)z_param` (following the self-pointer).

### `>Z` — Output (DLL → interpreter)

The interpreter passes a pointer to a slot. The DLL must:
1. Allocate a buffer containing the Z payload
2. Write the buffer's address into the slot: `*(intptr_t*)z_param = buffer_address`

The interpreter reads from the redirected address after the function returns.

**Important**: `>Z` still requires a corresponding APL argument (a dummy value, typically `0`).

### `=Z` — In/Out

Combines both: input arrives via the self-pointer mechanism (`<Z`), output via pointer redirect (`>Z`). The DLL reads the input from `*(intptr_t*)z_param`, then overwrites `*(intptr_t*)z_param` with the address of a freshly allocated output buffer.

### Scalar argument requirement

Each `⎕NA` parameter corresponds to exactly one element of the APL argument array. A function declared with a single `=Z` parameter expects a scalar argument. Since character vectors are arrays, they must be enclosed: `fn ⊂'hello'` (makes the 5-element vector into a scalar).

---

## Memory Management

### Allocation

The DLL allocates Z output buffers using any standard allocator. Both `HeapAlloc(GetProcessHeap(), ...)` and `NativeMemory.AllocZeroed(...)` (which uses the process heap on Windows) work correctly.

### Deallocation: `FreeUsedDyalogResult`

The interpreter probes for this export via `GetProcAddress` at DLL load time — before resolving any `⎕NA` function names. If found, the interpreter calls it after consuming each `>Z` or `=Z` output buffer:

```c
__declspec(dllexport) int FreeUsedDyalogResult(intptr_t ptr) {
    HeapFree(GetProcessHeap(), 0, (void*)ptr);
    return 1;
}
```

### DLL Load Protocol

When the interpreter loads any DLL via `⎕NA`, it probes two well-known exports:

1. **`FreeUsedDyalogResult`** — if found, cached for post-call Z/PP buffer cleanup
2. **`DyalogGetInterpreterFunctions`** — if found, called to inject the DWA function pointer table

---

## Edge Cases and Warnings

### Empty nested arrays require a prototype child

A Z buffer encoding a 0-element nested array (TYPEGEN + APLPNTR with shape containing a zero dimension) **must** include exactly 1 "phantom" child pocket after the shape — the **prototype**. This prototype determines the fill element structure used by APL's dyadic `↑` (take), `⌷` (index), etc.

If the prototype child is omitted, the interpreter crashes. With the prototype, the format is fully supported.

#### Wire layout for empty nested:

```
[Z header: 8 bytes]
[Root: wc | zones(TYPEGEN|APLPNTR) | shape (with zero dim)]
[Prototype: wc | zones | [shape] | data]    ← exactly 1 phantom child
```

The root `wc` includes 1 pointer slot for the prototype:
```
root_wc = (PocketHeader(16) + zones(8) + rank×8 + 1×pointer_slot(8)) / 8
```

#### Example: `0⍴⊂''` — 56 bytes

```
Offset  Qword   Meaning
──────  ─────   ───────
[0]     Z hdr   size=56 (BE), flags=0xA4 (BE)
[8]     5       root wc = (16+8+8+1×8)/8 = 5
[16]    0x0617  root zones: TYPEGEN|rank1|APLPNTR (no squoze)
[24]    0       root shape[0]: 0 elements
[32]    4       prototype wc (empty char vector)
[40]    0x271F  prototype zones: TYPESIMPLE|rank1|APLWCHAR8|squoze
[48]    0       prototype shape[0]: 0 chars
```

#### Prototype encoding rules (observed from Dyalog 20.0):

- `0⍴⊂''` → prototype is empty char vector (APLWCHAR8, shape=[0])
- `0⍴⊂⍬` → prototype is empty int vector (APLSINT, shape=[0])
- `0⍴⊂('name' 42)` → prototype is nested: `('    ' 0)` (spaces + zero)
- `(0 3)⍴⊂''` → rank-2, prototype is empty char vector

Note: The interpreter generates **type-correct prototypes** (spaces for chars, zeros for numbers). When constructing Z buffers, use the same convention.

### The `to_z` workspace function is unsafe in DWA context

The DWA SDK provides `to_z` in the workspace vtable. However, calling it from a DWA extension corrupts interpreter state and causes VALUE ERROR on subsequent Z outputs. Real Conga (and Conga-Sharp) build Z buffers directly without using `to_z`.

### Single characters are scalars

A single character like `'😀'` is a rank-0 scalar (no shape array), not a 1-element vector. The zones encode rank=0 and there is no shape field — the data follows immediately after zones.

---

## Relationship to `⎕NA` Character Types

`⎕NA` has three distinct mechanisms for passing text:

| `⎕NA` Type | C type | What happens |
|------------|--------|--------------|
| `T1` (`<0T1`) | `char*` (1-byte) | Raw characters at 1-byte width — no encoding. Each APL character's codepoint is truncated to 8 bits. |
| `T2` (`<0T2`) | `wchar_t*` (2-byte, Windows) | Raw characters at 2-byte width — no encoding. |
| `T4` (`<0T4`) | `wchar_t*` (4-byte, Unix) | Raw characters at 4-byte width — no encoding. |
| `T` (no width) | Platform default | `T2` on Windows, `T4` on Unix/macOS. |
| `UTF8` (`<0UTF8[]`) | `char*` | **Interpreter performs UTF-8 encoding/decoding** at the boundary. |
| `UTF16` (`<0UTF16[]`) | `uint16_t*` | **Interpreter performs UTF-16 encoding/decoding** at the boundary. |
| `Z` | Z buffer | Serialised APL array with full type/rank/shape metadata. Characters stored as raw codepoints at squeezed width (8/16/32 bit). |

Key distinctions:

- **`T1` is NOT UTF-8.** It passes raw byte values. Characters with codepoints > 255 cause a DOMAIN ERROR. The C function receives a `char*` with no encoding applied.
- **`UTF8` IS UTF-8.** The interpreter encodes APL characters into a UTF-8 byte sequence on output (`<0UTF8[]`) and decodes UTF-8 bytes back to APL characters on input (`>0UTF8[]`).
- **`Z` preserves everything.** Type, rank, shape, nesting — the full APL array structure crosses the boundary intact.

For explicit UTF-8 conversion within APL (without `⎕NA`), use `'UTF-8' ⎕UCS` (encode characters to UTF-8 byte vector) and `'UTF-8' ⎕UCS` with a byte vector argument (decode).

### Example: `T1` vs `UTF8` for the string `'café'`

```apl
⍝ Using T1: raw bytes, no encoding
⎕NA 'lib|func <0T1[] I'
func 'café' 4    ⍝ C receives: 63 61 66 E9 (4 bytes, 'é' is raw codepoint 0xE9)

⍝ Using UTF8: interpreter applies UTF-8 encoding
⎕NA 'lib|func <0UTF8[] I'
func 'café' 5    ⍝ C receives: 63 61 66 C3 A9 (5 bytes, 'é' is UTF-8 two-byte sequence)

⍝ Using T2: raw 16-bit codepoints (Windows wchar_t)
⎕NA 'lib|func <0T2[] I'
func 'café' 4    ⍝ C receives: 0063 0061 0066 00E9 (4 × 16-bit words)

⍝ T1 with characters > U+00FF: DOMAIN ERROR!
⎕NA 'lib|func <0T1[] I'
func '日本語' 3   ⍝ *** DOMAIN ERROR *** (codepoints don't fit in 1 byte)

⍝ UTF8 handles all of Unicode:
⎕NA 'lib|func <0UTF8[] I'
func '日本語' 9   ⍝ C receives: E6 97 A5 E6 9C AC E8 AA 9E (3 chars → 9 UTF-8 bytes)
```

The `T1` path raises DOMAIN ERROR for characters > U+00FF. The `UTF8` path handles the full Unicode range but the C function must be prepared for multi-byte sequences. Neither `T1` nor `UTF8` is "better" — they serve different purposes:
- Use `T1` when interfacing with legacy APIs that expect Latin-1 / raw byte strings
- Use `UTF8` when interfacing with modern APIs that expect UTF-8 encoded strings (most C libraries)
- Use `Z` when you need to preserve the full APL array structure (type, rank, shape, nesting)

---

## Implementation Notes

### Writing Z buffers (DLL → APL)

1. Choose the element type (can always use the widest: APLLONG for ints, APLWCHAR32 for chars)
2. Compute total size: `8 + 8 + 8 + rank×8 + Pad8(data_bytes)`
3. Allocate zeroed buffer of that size
4. Write Z header (big-endian size, big-endian flags=0xA4)
5. Write wc, zones, shape, data (all native-endian)
6. Set the output pointer

### Reading Z buffers (DLL ← APL)

1. Follow the self-pointer: `z_buf = *(uint8_t**)z_param`
2. Read total_size from bytes 0–3 (big-endian)
3. Read wc at offset 8, zones at offset 16 (native-endian)
4. Extract type, rank, eltype from zones
5. Read shape (rank × int64 starting at offset 24)
6. Read data starting at offset 24 + rank×8
7. For nested: recursively parse each child pocket after the root's shape

### Padding formula

```
Pad8(n) = (n + 7) & ~7    // round up to next 8-byte boundary
```

Data bytes for each element type:
- APLBOOL: `ceil(element_count / 8)` bytes (then padded to 8)
- APLSINT/APLWCHAR8: `element_count` bytes
- APLINTG/APLWCHAR16: `element_count × 2` bytes
- APLLONG/APLWCHAR32: `element_count × 4` bytes
- APLDOUB/APLQUAD/APLPNTR: `element_count × 8` bytes
- APLCMPX/APLDECF: `element_count × 16` bytes
