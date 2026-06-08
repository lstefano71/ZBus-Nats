# NATS adapter as a single ZBus adapter with shape-driven Z conventions

We will expose the full NATS ecosystem (Core pub/sub, JetStream, KV, Object Store, Services) through a single `nats` adapter in one AOT-compiled DLL (`ZBus.Nats`). Connection is explicit and non-blocking (`zbus_nats_connect` returns immediately; `Connected`/`Error` events arrive via Wait). All create verbs follow the existing pattern: user-supplied leaf name for identity (empty = auto-generated), NATS-specific identifiers (subjects, queue groups) are properties or parameters — never names.

Structured optional data (headers, queue groups, consumer options, stream config) uses shape-driven `=Z` dispatch: simple char vector for the common case, 2-element nested vector for one extra dimension, Nx2 nested matrix for a full options bag. This avoids verb proliferation and matches Conga's Nx2 header convention.

When a verb has both Z input and Z output, the `=Z` slot is reused: read input first, then overwrite with output. This eliminates `>Z` placeholder arguments. See `docs/nats-api-reference.md` for the full verb inventory.

## Considered Options

- **Multiple adapters** (one per NATS layer): rejected because all layers share one `NatsClient` connection. Splitting forces awkward cross-adapter references.
- **Blocking `zbus_nats_connect` with `&`**: rejected because the verb creates no new object (root already exists from `zbus_init`), so there's nothing to return that the caller needs before issuing Wait.
- **Dedicated reply verb**: rejected — a reply is just a publish to the `replyTo` subject. APL covers can wrap this if desired.
- **Separate verbs for headers/queue groups**: rejected in favor of shape-driven overloading, which is zero-cost ergonomically (APL strands are already nested at the call boundary).
- **`>Z` + placeholder for verbs with Z input**: rejected — reuse `=Z` instead. Read input first, then overwrite with output. Saves one arg and eliminates dummy `0` placeholders.

## Consequences

- `zbus_describe` is added as a new kernel export (adapter-delegated bulk introspection).
- `zbus_setprop` uses `<Z` (not `=Z`) — properties are write-only from APL's perspective (no return value beyond rc).
- Raw bytes only (`NatsRawSerializerRegistry.Default`) — no reflection-based serialization, AOT-safe.
