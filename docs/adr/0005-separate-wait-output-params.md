# Separate output parameters for Wait (not single >Z tuple)

The `zbus_wait` ⎕NA export uses separate output parameters for each element of the event tuple:

```
'wait' ⎕NA 'I4 ZBus.Echo|zbus_wait& <0T1 I4 >0T1 >0T1 >Z'
⍝         rc                         root ms  obj  evt  data
```

The alternative was packing `objectName`, `eventType`, and `data` into a single `>Z` nested array. We chose separate params because:

1. **Avoids unnecessary Z serialization** for the two short string fields (object name, event type) — they're native `>0T1` (null-terminated wide strings), zero-copy from the kernel's internal representation.
2. **Allows routing without deserializing data** — APL code (or the cover namespace) can inspect object name and event type without touching the potentially large data payload.
3. **Consistent with Conga-Sharp's approach** which uses the same multi-output pattern.

The raw ⎕NA call requires placeholders (`wait 'root' 5000 '' '' 0`), but the APL cover namespace hides this behind a clean `Wait 'root' 5000` → `(rc obj evt data)` interface.
