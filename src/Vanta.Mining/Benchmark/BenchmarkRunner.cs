using System.Collections.Concurrent;
using System.Diagnostics;
using Vanta.Core;

namespace Vanta.Mining;

public sealed class BenchmarkResult
{
    public required int Threads { get; init; }
    public required TimeSpan Duration { get; init; }
    public required long TotalHashes { get; init; }
    public required double Hashrate { get; init; }
    public required double HashratePerThread { get; init; }
}

public static class BenchmarkRunner
{
    public static BenchmarkResult Run(int threads, TimeSpan duration)
    {
        using var cache = new RandomXCache();
        cache.Initialize(ReadOnlySpan<byte>.Empty);

        return Run(threads, duration, () => new RandomXAlgorithm(cache));
    }

    /// <summary>
    /// Runs one independent algorithm instance on each dedicated worker thread.
    /// This overload is primarily useful for algorithm implementations and tests that do not
    /// need RandomX's shared cache.
    /// </summary>
    public static BenchmarkResult Run(
        int threads,
        TimeSpan duration,
        Func<IMiningAlgorithm> algorithmFactory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(algorithmFactory);

        using var workersReady = new CountdownEvent(threads);
        using var startGate = new ManualResetEventSlim(false);
        var workers = new Thread[threads];
        var failures = new ConcurrentQueue<Exception>();
        var totalHashes = 0L;
        var runWorkers = 0;
        var stopwatch = new Stopwatch();

        for (var index = 0; index < threads; index++)
        {
            var workerIndex = index;
            workers[workerIndex] = new Thread(() =>
            {
                IMiningAlgorithm? algorithm = null;
                var ready = false;

                try
                {
                    algorithm = algorithmFactory();
                    algorithm.Initialize(ReadOnlySpan<byte>.Empty);
                    workersReady.Signal();
                    ready = true;

                    startGate.Wait();
                    if (Volatile.Read(ref runWorkers) == 0)
                    {
                        return;
                    }

                    var data = new byte[76];
                    var output = new byte[32];
                    var localHashes = 0L;
                    while (stopwatch.Elapsed < duration)
                    {
                        algorithm.Hash(data, output);
                        localHashes++;
                    }

                    Interlocked.Add(ref totalHashes, localHashes);
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
                finally
                {
                    if (!ready)
                    {
                        workersReady.Signal();
                    }

                    algorithm?.Dispose();
                }
            })
            {
                IsBackground = true,
                Name = $"Vanta benchmark worker {workerIndex}"
            };
            workers[workerIndex].Start();
        }

        workersReady.Wait();
        if (!failures.IsEmpty)
        {
            startGate.Set();
            JoinWorkers(workers);
            throw new AggregateException("One or more benchmark workers could not initialize.", failures);
        }

        stopwatch.Start();
        Volatile.Write(ref runWorkers, 1);
        startGate.Set();
        JoinWorkers(workers);
        stopwatch.Stop();

        if (!failures.IsEmpty)
        {
            throw new AggregateException("One or more benchmark workers failed.", failures);
        }

        var elapsed = stopwatch.Elapsed <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : stopwatch.Elapsed;
        var hashrate = HashrateCalculator.Calculate(totalHashes, elapsed);

        return new BenchmarkResult
        {
            Threads = threads,
            Duration = elapsed,
            TotalHashes = totalHashes,
            Hashrate = hashrate,
            HashratePerThread = hashrate / threads
        };
    }

    private static void JoinWorkers(IEnumerable<Thread> workers)
    {
        foreach (var worker in workers)
        {
            worker.Join();
        }
    }
}
