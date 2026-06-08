# Z Format

A C# library for reading and writing Dyalog APL's Z wire format — the binary encoding used by `⎕NA` to pass APL arrays to and from external DLLs.

## Language

**Pocket**:
A contiguous block of memory holding one APL array value in wire format: a header (wc + zones), shape, and data. Nested arrays contain child pockets inline.
_Avoid_: buffer, packet, frame

**Zones**:
The 64-bit metadata field inside a pocket that encodes rank, element type, and nesting. An internal wire-format concept — not exposed to consumers.
_Avoid_: header, flags, descriptor

**Element type**:
The physical storage type of array elements on the wire (APLSINT, APLLONG, APLDOUB, APLWCHAR8, etc.). Exposed to power users via `ElType`.
_Avoid_: data type, value type

**Type family**:
The coarse consumer-facing classification of a Z value by its element domain: Bool, Byte, Char, Int, Double, Decf, Nested. Exposed as `ZType`. Rank is never part of the family.
_Avoid_: kind, category, variant

**Squeeze**:
Dyalog's internal process of narrowing storage to the smallest element type that fits the data (e.g., APLLONG → APLSINT if all values fit in a byte). May happen on return from `⎕NA`.
_Avoid_: compress, compact

**Prototype**:
The structure carried by an empty nested array that determines what fill elements look like (revealed by dyadic `↑`). In the Z wire format, encoded as a "phantom child" pocket serialized after the shape despite element count being 0. Omitting it crashes the interpreter.
_Avoid_: template, default
