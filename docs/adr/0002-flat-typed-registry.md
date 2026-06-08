# Flat typed registry with adapter-defined hierarchies

ZBus uses a flat dotted-name registry where each adapter defines its own object types and parent-child relationships. The kernel enforces naming uniqueness, lifecycle events, and hierarchical event delivery — but does not prescribe a fixed object hierarchy (like Root→Server→Connection).

The alternative was a universal hierarchy (Root→Provider→Channel→Message) but this would leak TCP assumptions into pub/sub adapters like NATS, where the topology is fundamentally different. A flat registry with adapter freedom keeps the kernel protocol-agnostic while still providing consistent naming and event semantics.
