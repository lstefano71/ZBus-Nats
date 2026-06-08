# ZBus

A generic event-bus framework for exposing .NET libraries to Dyalog APL via `⎕NA`, using the Z wire format for marshalling, textual hierarchical handles for object identity, and a Wait-based event loop for non-blocking I/O.

## Language

### Z Wire Format

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

### Bus Kernel

**Root**:
The top-level ZBus instance, identified by its name (the first segment of any dotted path). Owns the object registry, waitpoint tree, and adapter set. Multiple roots can coexist in one process, each with independent state. Created idempotently via `zbus_init`; closed via `zbus_close` (cascading).
_Avoid_: context, session, instance, handle

**Adapter**:
A compiled-in .NET module that extends ZBus with domain-specific object types and verb exports (e.g., NATS, TCP). Registers at compile time via explicit registration — no runtime discovery.
_Avoid_: plugin, provider, driver

**Object**:
A named entity in the registry with a lifecycle (Created → Started → Error → Closed). Owned by an adapter. Identity is its dotted name.
_Avoid_: handle, resource, entity

**Dotted name**:
The hierarchical identity of an object, expressed as a dot-separated path from the root (e.g., `N1.prices`). Parent-child relationships are encoded in the name structure. External identifiers (like NATS subjects) are properties, never names.
_Avoid_: path, handle, key

**Waitpoint**:
A queue associated with a dotted name from which `Wait` can dequeue events. Every named object implicitly has a waitpoint. Events bubble from child to ancestor waitpoints unless delivered as targeted.
_Avoid_: listener, subscription, channel

**Event tuple**:
The fixed 4-element array returned by Wait: `(rc, objectName, eventType, data)`. rc is always 0 for successfully dequeued events; domain information lives in eventType and data. The shape is adapter-agnostic.
_Avoid_: message, notification, signal

**General delivery**:
Event posting mode where the event goes to the deepest waitpoint with an active waiter, bubbling up the hierarchy. If no waiter is active, queues at the target.
_Avoid_: broadcast, fan-out

**Targeted delivery**:
Event posting mode where the event is only visible to a waiter on the exact matching waitpoint. Used for request/reply mailboxes. Never bubbles. If no waiter comes, the event stays until timeout or close.
_Avoid_: direct, pinned

**Verb export**:
An adapter-specific `[UnmanagedCallersOnly]` function exposed from the AOT DLL (e.g., `zbus_nats_subscribe`). Kernel exports are prefixed `zbus_`; adapter exports are prefixed `zbus_{adapterId}_`.
_Avoid_: API, function, command

**Shape-driven overload**:
A `=Z` parameter whose meaning is determined by the Z value's structure at runtime: simple char vector for the common case, 2-element nested vector for one extra dimension, Nx2 nested matrix for a full options bag. Avoids verb proliferation.
_Avoid_: polymorphic parameter, variant, union

**Describe**:
A kernel verb that returns a multi-element vector providing a textual description of a named object (type, state, adapter-specific metadata). One round-trip for full introspection. Inspired by Conga's `DRC.Describe`.
_Avoid_: info, inspect, dump
