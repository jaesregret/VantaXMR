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
    public static BenchmarkResult Run(int threads, TimeSpan duration, IMiningAlgorithm? algorithm = null)
    {
        var effectiveAlgorithm = algorithm ?? new RandomXAlgorithm();
        effectiveAlgorithm.Initialize(ReadOnlySpan<byte>.Empty);

        try
        {
            var totalHashes = 0L;
            var stopwatch = Stopwatch.StartNew();
            var data = new byte[76];
            var output = new byte[32];
            while (stopwatch.Elapsed < duration)
            {
                effectiveAlgorithm.Hash(data, output);
                totalHashes += 1;
            }

            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : stopwatch.Elapsed;
            var hashrate = HashrateCalculator.Calculate(totalHashes, elapsed);

            return new BenchmarkResult
            {
                Threads = threads,
                Duration = elapsed,
                TotalHashes = totalHashes,
                Hashrate = hashrate,
                HashratePerThread = threads > 0 ? hashrate / threads : hashrate
            };
        }
        finally
        {
            effectiveAlgorithm.Dispose();
        }
    }
}
