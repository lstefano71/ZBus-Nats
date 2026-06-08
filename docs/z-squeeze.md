# Z Format: Squeeze Behaviour

Empirical findings from testing with Dyalog 20.0 and the ZProbeManaged DLL.

## Key Findings

### 1. Scalars are always squeezed on receipt

Regardless of what element type we encode in the Z buffer (APLSINT, APLINTG, APLLONG), 
the interpreter squeezes scalar values to the smallest type that fits immediately upon 
return from `⎕NA`.

```
APLSINT scalar 42:  ⎕DR=83  (correct, already smallest)
APLINTG scalar 42:  ⎕DR=83  (squeezed from 16-bit to 8-bit)
APLLONG scalar 42:  ⎕DR=83  (squeezed from 32-bit to 8-bit)
```

**Implication:** Auto-squeezing on the write side (our `FromInt` method) is harmless for 
scalars — the interpreter would squeeze anyway. No performance penalty for sending the 
smallest representation.

### 2. Vectors retain oversized storage (squoze flag)

When we send a vector at a wider type than needed (e.g., APLLONG for values 1,2,3), 
the interpreter does NOT immediately squeeze it:

```
APLLONG vec [1,2,3]: ⎕DR=83  181⌶=323
```

- `⎕DR` = 83 — monadic `⎕DR` *attempts* a squeeze and reports what it *could* be
- `181⌶` = 323 — reports actual storage WITHOUT squeezing

Our Z format sets the `squoze` bit in zones (bit 13 = 1), which tells APL "this has 
already been through squeeze logic — don't re-squeeze on receipt." The interpreter 
honours this: the data stays at APLLONG width in memory.

However, workspace compaction WILL squeeze it eventually. The `squoze` flag only 
prevents the initial squeeze-on-receipt.

### 3. Dyadic `⎕DR` pinning survives Z round-trip

Values pinned with dyadic `⎕DR` (e.g., `⊃ 0 645 ⎕DR 42`) maintain their type through 
a complete Z echo (read → write → return):

```
Pinned 42→645:     ⎕DR=645  181⌶=645
After echo:        ⎕DR=645  181⌶=645

Pinned 1 2 3→323:  ⎕DR=323  181⌶=323
After echo:        ⎕DR=323  181⌶=323
```

Dyadic `⎕DR` sets the "sticky" bit (bit 12) which permanently prevents squeeze. 
Our Z reader/writer faithfully preserves this through the zones field.

### 4. The squoze bit semantics

From `Zones.cs`: bit 13 = squoze, bit 12 = sticky.

| Flag    | Meaning |
|---------|---------|
| squoze=1, sticky=0 | "Already squeezed" — don't re-squeeze on receipt, but compaction may still squeeze later |
| squoze=0, sticky=1 | "Pinned" — never squeeze (set by dyadic `⎕DR`) |
| squoze=1, sticky=1 | Invalid? (bridge-dwa clears squoze when setting sticky) |

## Implications for ZValue API

1. **`FromInt(long)` auto-squeeze is correct** — sending smallest representation is 
   optimal because:
   - Scalars: interpreter squeezes anyway
   - Vectors: smaller = less wire bytes, and APL will accept any width

2. **`FromInts(ReadOnlySpan<long>)` auto-squeeze is also fine** — same reasoning for vectors.
   Less data on the wire, interpreter doesn't care.

3. **Explicit-width factories (`FromInt32`, etc.) are useful when:**
   - Caller wants to guarantee a specific `181⌶` width on the APL side
   - Working with pinned values (dyadic `⎕DR` users expect exact types)
   - Performance: avoiding double-squeeze (we squeeze, then APL squeezes again on compaction)

4. **The `squoze` flag we set in `Zones.Simple()`** is correct behaviour. It tells APL 
   "this data is already at its natural width" which prevents an unnecessary re-squeeze 
   on receipt.

5. **Sticky bit**: We do NOT set it currently. This means values CAN be squeezed by 
   workspace compaction. If a consumer needs permanent type pinning (matching dyadic 
   `⎕DR` behaviour), we'd need a `sticky: true` option on factory methods.

## ⎕DR Code Reference

| Code | Type | Bytes | APL Name |
|------|------|-------|----------|
| 11   | Boolean | 1 bit | APLBOOL |
| 83   | Int8 | 1 | APLSINT |
| 163  | Int16 | 2 | APLINTG |
| 323  | Int32 | 4 | APLLONG |
| 645  | Float64 | 8 | APLDOUB |
| 1287 | Decimal128 | 16 | APLDECF |
| 80   | Char8 | 1 | APLWCHAR8 |
| 160  | Char16 | 2 | APLWCHAR16 |
| 320  | Char32 | 4 | APLWCHAR32 |
| 326  | Pointer | ptr | APLPNTR |
