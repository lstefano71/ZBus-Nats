# ZBus.Nats

A NATS messaging client for Dyalog APL, built on the [ZBus](docs/adr/0001-zbus-generic-event-bus-architecture.md) generic event-bus framework and [NATS.Net](https://github.com/nats-io/nats.net).

Ships as a single **NativeAOT DLL** (~8 MB) loadable via `⎕NA` — no .NET runtime installation required.

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
