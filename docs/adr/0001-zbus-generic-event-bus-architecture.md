# ZBus: generic event-bus architecture for ⎕NA

We chose to build ZBus as a generic framework — not a TCP-specific library — that any .NET library can plug into via an adapter pattern. The kernel provides: a dotted-name object registry, hierarchical waitpoints with general/targeted delivery, Z-format marshalling, and `&`-compatible ⎕NA exports. Adapters register at compile time and AOT-compile into a single DLL per adapter (`ZBus.{Adapter}.dll`).

The alternative was to keep building single-purpose ⎕NA DLLs (like Conga-Sharp) for each library. We rejected this because the infrastructure — object registry, event queue, Wait semantics, Z marshalling, handle naming — is identical across use cases. Duplicating it per library guarantees drift and multiplies maintenance.

## Considered Options

- **Extract kernel from Conga-Sharp** — rejected because the framework should be independently consumable; Conga-Sharp will become a future adapter.
- **Runtime plugin discovery** — rejected because NativeAOT makes `Assembly.Load` impractical and compile-time composition is simpler and safer.
- **Fixed object hierarchy (Root→Provider→Channel→Message)** — rejected because it bakes TCP assumptions into the model. A flat typed registry lets each adapter define its own hierarchy.

## Consequences

- ZFormat remains a standalone library within the ZBus repo (no dependency on the bus kernel).
- Every adapter DLL includes kernel exports (`zbus_init`, `zbus_wait`, etc.) plus adapter-specific verb exports (`zbus_{id}_*`).
- Conga-Sharp can be refactored as a ZBus adapter in the future, but is not blocked by this work.
- The echo/timer adapter is the first validation target before real adapters (NATS).
