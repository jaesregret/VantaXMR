using System.Threading;
using Vanta.Mining;

namespace Vanta.Mining.Tests;

public sealed class BenchmarkRunnerTests
{
    [Fact]
    public void Run_CreatesAndRunsOneIndependentAlgorithmPerWorker()
    {
        const int workers = 4;
        var state = new ConcurrencyState(workers);
        var created = 0;

        var result = BenchmarkRunner.Run(
            workers,
            TimeSpan.FromMilliseconds(100),
            () =>
            {
                Interlocked.Increment(ref created);
                return new CoordinatedAlgorithm(state);
            });

        Assert.Equal(workers, created);
        Assert.Equal(workers, state.InitializedAlgorithms);
        Assert.Equal(workers, state.MaximumConcurrentFirstHashes);
        Assert.True(result.TotalHashes >= workers);
    }

    [Fact]
    public void Run_StopsWorkersIfInitializationFails()
    {
        var exception = Assert.Throws<AggregateException>(() => BenchmarkRunner.Run(
            2,
            TimeSpan.FromSeconds(1),
            static () => new FailingAlgorithm()));

        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    private sealed class CoordinatedAlgorithm(ConcurrencyState state) : IMiningAlgorithm
    {
        private int _firstHash = 1;

        public string Name => "Coordinated test algorithm";

        public void Initialize(ReadOnlySpan<byte> seedHash)
        {
            Interlocked.Increment(ref state.InitializedAlgorithms);
        }

        public void Hash(ReadOnlySpan<byte> input, Span<byte> output)
        {
            output.Clear();
            if (Interlocked.Exchange(ref _firstHash, 0) == 0)
            {
                return;
            }

            state.FirstHashes.Signal();
            if (!state.FirstHashes.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Not all benchmark workers entered Hash().");
            }

            var active = Interlocked.Increment(ref state.ActiveFirstHashes);
            state.ActiveFirstHashesReady.Signal();
            if (!state.ActiveFirstHashesReady.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Benchmark worker hashes did not overlap.");
            }

            UpdateMaximum(ref state.MaximumConcurrentFirstHashes, active);
            Interlocked.Decrement(ref state.ActiveFirstHashes);
        }

        public void Dispose() { }
    }

    private sealed class ConcurrencyState(int workers)
    {
        public CountdownEvent FirstHashes { get; } = new(workers);
        public CountdownEvent ActiveFirstHashesReady { get; } = new(workers);
        public int InitializedAlgorithms;
        public int ActiveFirstHashes;
        public int MaximumConcurrentFirstHashes;
    }

    private sealed class FailingAlgorithm : IMiningAlgorithm
    {
        public string Name => "Failing test algorithm";

        public void Initialize(ReadOnlySpan<byte> seedHash) => throw new InvalidOperationException("Expected test failure.");

        public void Hash(ReadOnlySpan<byte> input, Span<byte> output) => throw new NotSupportedException();

        public void Dispose() { }
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (current >= value || Interlocked.CompareExchange(ref maximum, value, current) == current)
            {
                return;
            }
        }
    }
}
