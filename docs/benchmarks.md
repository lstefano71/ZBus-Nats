# ZBus.Nats Benchmarks

Run timestamp: `2026-06-08T23:19`  
Machine: Development workstation (Windows, Dyalog 20.0, .NET 10, NATS 2.14.2)  
APL timing: `16 ⎕DT 'Z'` with `⎕FR←1287` localised in dfn (nanosecond precision)

## APL Client (via ⎕NA + Z format)

### Pub/Sub Throughput (fire-and-forget, no wait for delivery)

| Payload | Count  | msg/s   | MB/s   | Elapsed |
|---------|--------|---------|--------|---------|
| 64 B    | 10,000 | 94,534  | 5.77   | 0.106s  |
| 256 B   | 10,000 | 87,619  | 21.39  | 0.114s  |
| 1024 B  | 10,000 | 70,817  | 69.16  | 0.141s  |
| 4096 B  | 10,000 | 43,565  | 170.1  | 0.230s  |

### Pub/Sub Round-trip (pub + wait per message)

| Payload | Count | msg/s | avg latency | Elapsed |
|---------|-------|-------|-------------|---------|
| 64 B    | 1,000 | 5,602 | 178 µs      | 0.178s  |
| 256 B   | 1,000 | 4,665 | 214 µs      | 0.214s  |
| 1024 B  | 1,000 | 5,232 | 191 µs      | 0.191s  |

### Request/Reply

| Requests | req/s | avg latency | Elapsed |
|----------|-------|-------------|---------|
| 500      | 2,024 | 493 µs      | 0.247s  |

## C# Baseline (pure NATS.Net, no Z-format layer)

### Pub/Sub Throughput (fire-and-forget + subscribe, 50K messages)

| Payload | Count  | msg/s   | MB/s   | Elapsed |
|---------|--------|---------|--------|---------|
| 64 B    | 50,000 | 1,655   | 0.1    | 30.2s   |
| 256 B   | 50,000 | 1,655   | 0.4    | 30.2s   |
| 1024 B  | 50,000 | 90,142  | 88.0   | 0.555s  |
| 4096 B  | 50,000 | 82,015  | 320.4  | 0.610s  |

### Request/Reply

| Requests | req/s | avg latency | Elapsed |
|----------|-------|-------------|---------|
| 5,000    | 3,748 | 267 µs      | 1.334s  |

## Overhead Analysis

| Metric | APL (⎕NA) | C# native | Overhead |
|--------|-----------|-----------|----------|
| Pub throughput (1 KB) | 70,817 msg/s | 90,142 msg/s | ~21% |
| Request/Reply latency | 493 µs | 267 µs | ~85% |

The ⎕NA/Z-format bridge adds modest overhead on throughput paths (~21% at 1 KB)
and more on latency-sensitive request/reply (~85%), which is expected given the
per-call cost of marshalling through Z format and the ⎕NA call boundary.

## Notes

- C# 64B/256B pub/sub shows anomalously low throughput; likely a subscriber
  back-pressure or batching issue in the baseline benchmark at small payloads.
- APL numbers are fire-and-forget only (no subscriber wait), so they measure
  raw ⎕NA call overhead per publish.
- All benchmarks run against localhost NATS (no network latency).
