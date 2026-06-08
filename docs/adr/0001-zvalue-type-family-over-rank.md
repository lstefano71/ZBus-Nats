# ZValue public API: type family over rank

The `ZValueKind` enum conflated two orthogonal axes — rank (scalar vs array) and element type (char, int, double). This forced consumers to handle `Scalar` as a separate case despite scalars being ordinary rank-0 arrays in APL. We decided to replace it with a `ZType` enum that discriminates purely by element-type family, and to reshape the public API accordingly.

## Considered Options

**A. Keep `ZValueKind` with separate `Scalar` variant.** Status quo. Convenient "is this a scalar?" check, but semantically wrong — a scalar integer and a vector integer are the same domain, differing only in shape. Forces redundant branching in consumer code.

**B. Remove discriminant entirely, expose only `ElType` + `Shape`.** Pure, but forces consumers to write their own mapping from 12+ raw element types to the 6 logical families (e.g., APLWCHAR8/16/32 all mean "characters").

**C. (Chosen) `ZType` enum: Bool, Byte, Char, Int, Double, Decf, Nested.** Rank orthogonal — always check `Shape.Length`. Gives consumers a fast switch over logical families without dealing with storage-width variants.

## Consequences

Public API shape of `ZValue` becomes:

- `ZType Type` — coarse family (from `ZType` enum)
- `ElType ElType` — exact storage type (for power users)
- `long[] Shape` — rank and dimensions; `Shape.Length == 0` means scalar
- `Zones` — internal (only `ZWriter`/`ZReader` use it)
- `RawData` — internal (typed span accessors are the public contract)
- Indexer `this[int]` on nested values (alongside `Children` span)
- Factory naming: singular = scalar (`FromInt`, `FromChar`, `FromDouble`), plural = array (`FromInts`, `FromChars`, `FromDoubles`)
- `FromInts` auto-squeezes to smallest element type; explicit-width factories (`FromInt8`, `FromInt16`, `FromInt32`) remain as escape hatches
- Empty-nested guard moves from `ZValue` constructor to `ZWriter`
- Typed span accessors (`ReadOnlySpan<T>`) for zero-copy access where element type matches; allocating widening methods (e.g., `ToInt64Array()`) clearly named to signal cost
