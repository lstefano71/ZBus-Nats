# ZBus.Nats — Future Features (v1.5+)

## Authentication & Security

- **Auth options via SetProperty** — support before `nats_connect`:
  - `Token` — simple token auth
  - `User` / `Password` — user/pass
  - `CredsFile` — path to `.creds` file (NKey + JWT)
  - `NKeyFile` — path to NKey seed file
  - `TlsMode` — `'require'` | `'verify'` | `'off'`
  - `TlsCertFile` / `TlsKeyFile` / `TlsCaFile` — mTLS paths
- **Reconnect tuning** — `MaxReconnectAttempts`, `ReconnectWaitMs`, `ReconnectJitterMs`

## APL Covers (Namespace API)

- Proper `ZBus` namespace with operator-style covers:
  ```apl
  ZBus.Connect 'nats://localhost:4222'
  ZBus.Pub 'subject' payload
  msg←ZBus.Sub 'pattern' :Wait 5000
  ```
- Managed lifetime (auto-close on `)CLEAR` or `⎕OFF`)
- Helper dfns for common patterns: request/reply, fan-out, stream ingest
- Error formatting: translate rc codes to human-readable messages

## Stream & Consumer Management

- `nats_stream_info` — return message count, byte size, first/last seq
- `nats_consumer_info` — pending count, ack floor, last delivered
- `nats_stream_update` — update retention/limits without recreating
- Consumer pause/resume
- Ordered consumers (ephemeral, replay)

## Performance

- Batch publish (`nats_jspub_batch`) — publish N messages in one ⎕NA call
- Batch ack — ack multiple seqs in one call
- Configurable subscription channel capacity via SetProperty (per-sub override)
- Zero-copy payload passthrough (skip ZValue allocation for large payloads)

## Observability

- `nats_stats` — connection statistics (msgs in/out, bytes in/out, reconnects)
- Structured error events with error codes (not just strings)
- Optional trace mode (log all ⎕NA calls with timing to a file)
- Health check endpoint integration

## KV & Object Store Enhancements

- KV `history` — retrieve revision history for a key
- KV `keys` — list all keys in a bucket
- KV bucket config options (max history, TTL, replicas)
- Object store: streaming get/put for large objects (chunked)
- Object store metadata (custom headers per object)

## Services (Micro) Enhancements

- Service stats/info introspection from APL
- Multiple endpoints per service with routing
- Service groups (logical grouping, shared queue)
- Graceful drain on service shutdown

## Other Adapters

The ZBus kernel is adapter-agnostic. NATS is the first adapter; others could reuse the
same `IAdapter` contract, ⎕NA export patterns, and event-loop idioms.

### ZBus.Redis

- **Client:** [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) (Marc Gravell)
- **AOT status:** Not yet officially AOT-compatible (uses reflection internally).
  Tracking progress — may become viable in a future release with source-generator support.
- **Fit:** Pub/Sub, Streams (consumer groups ≈ JetStream), key-value — natural mapping
  to the same ZBus event patterns. Redis Streams have explicit ack like JetStream.
- **Risk:** AOT compatibility is the main blocker. May need a fork or alternate client.

### ZBus.MQTT

- **Client:** [MQTTnet](https://github.com/dotnet/MQTTnet) (now under dotnet org)
- **AOT status:** Partial — core MQTT works with workarounds, advanced features use
  reflection. No official AOT guarantee yet (roadmap item).
- **Fit:** Topic-based pub/sub maps directly. QoS levels (0/1/2) give delivery
  guarantees. Retained messages ≈ KV. No native request/reply (needs correlation IDs).
- **Use case:** IoT edge, constrained devices, bridging APL to MQTT brokers (Mosquitto, HiveMQ).

### ZBus.ZeroMQ

- **Client:** [NetMQ](https://github.com/zeromq/netmq) (pure C#, no native deps)
- **AOT status:** Best candidate — pure managed code, no reflection, no native DLLs.
  Community reports of successful NativeAOT builds. Needs verification at scale.
- **Fit:** REQ/REP, PUB/SUB, PUSH/PULL, DEALER/ROUTER — maps beautifully to ZBus
  patterns. Brokerless (peer-to-peer) is a different deployment model but the API shape
  (connect, send, receive events) fits the waitpoint model perfectly.
- **Use case:** Low-latency intra-process/intra-machine messaging, no broker dependency.

### Adapter Viability Summary

| Adapter | .NET Client | AOT Ready? | Broker Required? | Best For |
|---------|-------------|------------|------------------|----------|
| **NATS** (done) | NATS.Net | ✅ Yes | Yes (lightweight) | General-purpose messaging |
| **ZeroMQ** | NetMQ | 🟡 Likely | No (brokerless) | Low-latency, no infrastructure |
| **Redis** | SE.Redis | 🔴 Not yet | Yes | Teams already running Redis |
| **MQTT** | MQTTnet | 🟡 Partial | Yes | IoT, edge devices |

## Deployment & Packaging

- NuGet package for the C# library (ZFormat + ZBus + ZBus.Nats)
- Linux AOT build (`.so` for headless APL on Linux)
- ARM64 build (macOS Apple Silicon, Linux ARM)
- Versioned DLL with embedded version queryable via `describe`

## Developer Experience

- `nats_diag` verb — dump full connection state, subscription list, pending counts
- Better error messages: map NATS server error codes to actionable descriptions
- Example scripts for common patterns:
  - Work queue (competing consumers)
  - Event sourcing (ordered replay)
  - Request/reply service mesh
  - KV-based config hot reload
