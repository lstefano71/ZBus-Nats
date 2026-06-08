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
    await Task.Delay(500);

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
    await Task.WhenAny(tcs.Task, Task.Delay(10_000));
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

// ── Single-connection round-trip (pub + receive per message) ────────
Console.WriteLine("  Single-conn round-trip (pub + recv per msg):");
foreach (var payloadSize in new[] { 64, 256, 1024, 4096 })
{
    var payload = new byte[payloadSize];
    Random.Shared.NextBytes(payload);

    int count = payloadSize <= 256 ? 5000 : payloadSize <= 1024 ? 2500 : 1000;

    await using var rtConn = new NatsConnection(new NatsOpts { Url = url });
    await rtConn.ConnectAsync();

    var rtChan = System.Threading.Channels.Channel.CreateBounded<byte[]>(16384);
    var rtCts = new CancellationTokenSource();
    var rtSubTask = Task.Run(async () =>
    {
        await foreach (var msg in rtConn.SubscribeAsync<byte[]>("bench.rt", cancellationToken: rtCts.Token))
        {
            rtChan.Writer.TryWrite(msg.Data ?? []);
        }
    });

    await Task.Delay(50);

    // Warmup
    for (int i = 0; i < 100; i++)
    {
        await rtConn.PublishAsync("bench.rt", payload);
        await rtChan.Reader.ReadAsync();
    }

    // Timed
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < count; i++)
    {
        await rtConn.PublishAsync("bench.rt", payload);
        await rtChan.Reader.ReadAsync();
    }
    sw.Stop();

    double elapsed = sw.Elapsed.TotalSeconds;
    double rate = count / elapsed;
    double avgUs = (elapsed / count) * 1_000_000;
    Console.WriteLine($"    {payloadSize,5}B × {count:N0}: {rate:N0} msg/s, avg {avgUs:F0}µs ({elapsed:F3}s)");

    rtCts.Cancel();
    try { await rtSubTask; } catch (OperationCanceledException) { }
}

Console.WriteLine();

// ── Fan-out: 1 pub → N subs ────────────────────────────────────────
Console.WriteLine("  Fan-out (1 pub → N subs, round-trip per msg):");
foreach (var numSubs in new[] { 1, 2, 4 })
{
    const int fanSize = 256;
    const int fanCount = 2000;
    var payload = new byte[fanSize];
    Random.Shared.NextBytes(payload);

    var fanChans = new System.Threading.Channels.Channel<byte[]>[numSubs];
    var fanCts = new CancellationTokenSource();
    var fanTasks = new Task[numSubs];
    for (int s = 0; s < numSubs; s++)
    {
        fanChans[s] = System.Threading.Channels.Channel.CreateBounded<byte[]>(16384);
        var ch = fanChans[s];
        fanTasks[s] = Task.Run(async () =>
        {
            await foreach (var msg in subConn.SubscribeAsync<byte[]>("bench.fan", cancellationToken: fanCts.Token))
            {
                ch.Writer.TryWrite(msg.Data ?? []);
            }
        });
    }

    await Task.Delay(50);

    // Warmup
    for (int i = 0; i < 10; i++)
    {
        await pubConn.PublishAsync("bench.fan", payload);
        for (int s = 0; s < numSubs; s++)
            await fanChans[s].Reader.ReadAsync();
    }

    // Timed
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < fanCount; i++)
    {
        await pubConn.PublishAsync("bench.fan", payload);
        for (int s = 0; s < numSubs; s++)
            await fanChans[s].Reader.ReadAsync();
    }
    sw.Stop();

    double elapsed = sw.Elapsed.TotalSeconds;
    Console.WriteLine($"    {numSubs} subs × {fanCount:N0}: {fanCount / elapsed:N0} msg/s ({elapsed:F3}s)");

    fanCts.Cancel();
    for (int s = 0; s < numSubs; s++)
        try { await fanTasks[s]; } catch (OperationCanceledException) { }
}

Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════");
Console.WriteLine(" Benchmark complete");
Console.WriteLine("═══════════════════════════════════════════");
