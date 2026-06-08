using System.Diagnostics;
using NATS.Client.Core;

// Pure C# NATS baseline benchmark
// Measures raw pub/sub throughput without the ⎕NA/Z-format layer

const string url = "nats://localhost:4222";
const int warmup = 1000;

Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine(" C# NATS Baseline Benchmark");
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine();

await using var pubConn = new NatsConnection(new NatsOpts { Url = url });
await pubConn.ConnectAsync();

await using var subConn = new NatsConnection(new NatsOpts { Url = url });
await subConn.ConnectAsync();

Console.WriteLine("Connected to NATS");
Console.WriteLine();

// ── Pub/Sub throughput ──────────────────────────────────────────────

foreach (var payloadSize in new[] { 64, 256, 1024, 4096 })
{
    var payload = new byte[payloadSize];
    Random.Shared.NextBytes(payload);

    int received = 0;
    var tcs = new TaskCompletionSource();

    const int msgCount = 50_000;

    // Subscribe
    var cts = new CancellationTokenSource();
    var subTask = Task.Run(async () =>
    {
        await foreach (var msg in subConn.SubscribeAsync<byte[]>("bench.pub", cancellationToken: cts.Token))
        {
            if (Interlocked.Increment(ref received) >= msgCount)
            {
                tcs.TrySetResult();
                break;
            }
        }
    });

    // Small delay for sub to be ready
    await Task.Delay(100);

    // Warmup
    for (int i = 0; i < warmup; i++)
        await pubConn.PublishAsync("bench.pub", payload);
    await Task.Delay(50);
    Interlocked.Exchange(ref received, 0);

    // Timed publish
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < msgCount; i++)
        await pubConn.PublishAsync("bench.pub", payload);

    // Wait for all to arrive
    await Task.WhenAny(tcs.Task, Task.Delay(30_000));
    sw.Stop();

    cts.Cancel();

    double elapsed = sw.Elapsed.TotalSeconds;
    double msgsPerSec = msgCount / elapsed;
    double mbPerSec = (msgCount * (double)payloadSize) / (1024 * 1024) / elapsed;

    Console.WriteLine($"  Pub/Sub {payloadSize,5}B × {msgCount:N0}: {msgsPerSec:N0} msg/s, {mbPerSec:F1} MB/s ({elapsed:F3}s)");
}

Console.WriteLine();

// ── Request/Reply latency ───────────────────────────────────────────

Console.WriteLine("  Request/Reply latency:");
{
    var payload = new byte[64];

    // Start responder
    var respCts = new CancellationTokenSource();
    var respTask = Task.Run(async () =>
    {
        await foreach (var msg in subConn.SubscribeAsync<byte[]>("bench.req", cancellationToken: respCts.Token))
        {
            await msg.ReplyAsync(msg.Data);
        }
    });

    await Task.Delay(100);

    // Warmup
    for (int i = 0; i < 100; i++)
        await pubConn.RequestAsync<byte[], byte[]>("bench.req", payload);

    // Timed
    const int reqCount = 5_000;
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < reqCount; i++)
        await pubConn.RequestAsync<byte[], byte[]>("bench.req", payload);
    sw.Stop();

    respCts.Cancel();

    double elapsed = sw.Elapsed.TotalSeconds;
    double avgLatencyUs = (elapsed / reqCount) * 1_000_000;
    double reqsPerSec = reqCount / elapsed;

    Console.WriteLine($"    {reqCount:N0} requests: {reqsPerSec:N0} req/s, avg {avgLatencyUs:F0}µs ({elapsed:F3}s)");
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine(" Benchmark complete");
Console.WriteLine("═══════════════════════════════════════════");
