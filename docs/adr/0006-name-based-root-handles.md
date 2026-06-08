# Name-based root handles (no opaque pointers)

ZBus roots are identified by their name (a character string), not by opaque integer handles. The root name is the first segment of any dotted object path — `zbus_init 'MyBus'` creates a root, and all subsequent calls use `'MyBus'` or `'MyBus.child'` as the identifier. Init is idempotent: calling it twice with the same name returns the existing root.

The alternative was Conga-Sharp's approach: return a `P` (pointer-sized integer) from init and pass it to every call. We rejected this because the entire ZBus philosophy is "objects are character arrays." Making the root an exception would break uniformity and force APL code to carry an opaque integer. With `>Z` for outputs, there's no buffer-size limitation that would justify a fixed-size handle.

## Consequences

- Every kernel export takes `<0T1` (null-terminated ASCII) for the object name — root inferred from the first dot-segment.
- No HandleTable needed in the kernel. The root registry is keyed by name (case-insensitive).
- Closing the root (`zbus_close 'MyBus'`) cascades to all children — same function as closing any object.
- Max segment length is a logical constraint (32 chars for auto-generated names), not a buffer constraint.
