# Multiple >Z output parameters per export

ZBus ⎕NA exports use multiple `>Z` parameters for structured results rather than packing everything into a single `>Z` nested array or using fixed-size `>0T1` buffers.

For example, `zbus_wait` returns three `>Z` outputs: object name, event type, and data payload — each independently allocated by ZWriter on the C# side. This avoids buffer-size guessing (no need to pre-size `>0T1` placeholders) while keeping the outputs separated for efficient routing (APL can inspect the name without deserializing the data payload).

On error (rc≠0), all `>Z` output slots are written with valid empty arrays (`''` or `⍬`) so APL never dereferences garbage.
