# ZBus.Nats

A NATS messaging client for Dyalog APL, built on the [ZBus](docs/adr/0001-zbus-generic-event-bus-architecture.md) generic event-bus framework and [NATS.Net](https://github.com/nats-io/nats.net).

Ships as a single **NativeAOT DLL** (~8 MB) loadable via `⎕NA` — no .NET runtime installation required.

## Philosophy

### ZBus: A Generic Event Bus for APL

ZBus is not a NATS library — it's a **framework** for building event-driven adapters
that feel native to Dyalog APL. NATS is the first adapter; others (MQTT, Redis Streams,
WebSockets, gRPC) can follow the same patterns.

The design is inspired by [Conga](https://docs.dyalog.com/latest/Conga%20User%20Guide.pdf),
Dyalog's TCP/HTTP library. We adopted Conga's idioms and made them generic:

| Conga pattern | ZBus equivalent |
|---------------|-----------------|
| `DRC.New` | `zbus_init` — create a named root |
| `DRC.Wait` | `zbus_wait&` — block for next event (threaded) |
| `DRC.Close` | `zbus_close` — teardown with synthetic `Closed` event |
| `DRC.Describe` | `zbus_describe` — introspect objects |
| `DRC.GetProp` | `zbus_getprop` — query state |
| Dotted names (`srv.con0.data`) | Hierarchical object tree (`N1.ORDERS.proc`) |
| Event loop: `Wait` → dispatch on event type | Same — `(rc obj evt data)←wait ...` |

This means **any APL programmer who knows Conga already knows how to use ZBus**.
The learning curve is the adapter's domain (NATS subjects, JetStream streams),
not the mechanics of event-driven programming.

### Pluggable Adapters

The bus kernel ([`src/ZBus/`](src/ZBus/)) handles:
- **Object registry** — hierarchical named objects with lifecycle
- **Waitpoint tree** — event routing, buffering, targeted delivery
- **Z format I/O** — structured data across the ⎕NA boundary

Adapters implement [`IAdapter`](src/ZBus/IAdapter.cs) and plug into the kernel.
Each adapter owns its domain-specific connections and translates between
external protocols and ZBus events. See the [architecture ADR](docs/adr/0001-zbus-generic-event-bus-architecture.md)
for the full design.

### Why NativeAOT?

Dyalog APL's `⎕NA` loads plain C-callable DLLs. NativeAOT compiles .NET to native
code with `[UnmanagedCallersOnly]` exports — giving us the full .NET ecosystem
(async/await, NuGet packages, NATS.Net) behind a simple C ABI that APL can call directly.

## Features

- **Core NATS** — pub/sub, request/reply, queue groups, wildcards
- **JetStream** — persistent streams, consumers, ack/nak, configurable retention
- **Key/Value Store** — strongly-consistent KV with watch
- **Object Store** — large binary storage with change notifications
- **Services (Micro)** — register and discover NATS microservices
- **Diagnostics** — error ring buffer queryable via `getprop`

## Quick Start

```apl
dll←'path\to\ZBus.Nats.dll'

'init'    ⎕NA 'I4 ',dll,'|zbus_init <0T1 >Z'
'wait'    ⎕NA 'I4 ',dll,'|zbus_wait& <0T1 I4 >Z >Z >Z'
'close'   ⎕NA 'I4 ',dll,'|zbus_close <0T1'
'connect' ⎕NA 'I4 ',dll,'|zbus_nats_connect <0T1 <0T1'
'pub'     ⎕NA 'I4 ',dll,'|zbus_nats_pub <0T1 <0T1 <Z'
'sub'     ⎕NA 'I4 ',dll,'|zbus_nats_sub <0T1 <0T1 =Z'

(rc _)←init 'N1' 0
rc←connect 'N1' 'nats://localhost:4222'
(rc obj evt data)←wait 'N1' 5000 0 0 0    ⍝ → evt='Connected'

(rc subName)←sub 'N1' 'prices' 'market.>'
rc←pub 'N1' 'market.AAPL' 'price=150.25'
(rc obj evt data)←wait subName 3000 0 0 0  ⍝ → evt='Msg'
```

## Documentation

- [User Guide](docs/nats-user-guide.md) — tutorial-style walkthrough of all features
- [API Reference](docs/nats-api-reference.md) — complete ⎕NA signatures and event types
- [Future Features](docs/future-features.md) — roadmap for v1.5+
- [Z Wire Format](docs/z-wire-format.md) — the binary format used by `<Z` / `>Z` / `=Z`
- [Architecture Decisions](docs/adr/) — ADRs documenting key design choices

## Building

```powershell
# Requires .NET 10 SDK + MSVC linker (Visual Studio Build Tools)
dotnet publish src\ZBus.Nats\ZBus.Nats.csproj -c Release -r win-x64
# Output: src\ZBus.Nats\bin\Release\net10.0\win-x64\publish\ZBus.Nats.dll
```

## Testing

```powershell
# Unit tests (no NATS server needed)
dotnet test

# E2E APL tests (requires nats-server on localhost:4222)
.\apl\run_tests.ps1

# Benchmarks
.\apl\run_benchmarks.ps1
```

## Project Structure

```
src/ZFormat/        Z wire format reader/writer (standalone)
src/ZBus/          Event bus kernel (registry, waitpoints, adapters)
src/ZBus.Nats/     NATS adapter + AOT exports
tests/ZFormat.Tests/   Unit tests for Z format
tests/ZBus.Tests/      Unit tests for bus kernel (waitpoint delivery)
apl/               APL test & benchmark scripts
bench/             C# baseline benchmarks
docs/              Documentation
```

## Requirements

- [nats-server](https://nats.io/download/) 2.10+ (with `-js` for JetStream features)
- Dyalog APL 19.0+ (64-bit Unicode)
- .NET 10 SDK (build only — not needed at runtime)

## License

Proprietary. See LICENSE file.
