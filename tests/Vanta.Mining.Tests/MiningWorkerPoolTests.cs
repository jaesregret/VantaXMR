using Vanta.Core;
using Vanta.Mining;
using Vanta.Network;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace Vanta.Mining.Tests;

public class MiningWorkerPoolTests
{
    [Fact]
    public async Task WorkerPool_ProcessesJobAndStopsOnCancellation()
    {
        var algorithm = new TestMiningAlgorithm();
        var stats = new MiningStatistics();
        using var pool = new MiningWorkerPool(algorithm, 2, stats);
        var job = new PoolJob
        {
            JobId = "job-1",
            Blob = new string('0', 86),
            Target = "ffffffff",
            Difficulty = 1
        };

        await pool.StartAsync(CancellationToken.None);
        pool.AssignJob(job);

        await Task.Delay(50);

        Assert.True(stats.TotalHashes >= 1);
    }

    [Fact]
    public async Task WorkerPool_UsesFullEightByteTargetComparison()
    {
        var submitted = new TaskCompletionSource<PoolShare>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pool = new MiningWorkerPool(
            new FixedHashAlgorithm(0x0000000200000001UL),
            1,
            submitShare: (share, _) =>
            {
                submitted.TrySetResult(share);
                return Task.CompletedTask;
            });

        await pool.StartAsync(CancellationToken.None);
        pool.AssignJob(new PoolJob
        {
            JobId = "job-1",
            Blob = new string('0', 86),
            Target = "01000000",
            Difficulty = 1
        });

        await Task.Delay(100);

        Assert.False(submitted.Task.IsCompleted);
    }

    [Fact]
    public async Task WorkerPool_StartsIndependentWorkersAndAssignsDisjointNonceSequences()
    {
        const int workerCount = 4;
        var state = new WorkerState(workerCount);
        var submitted = new ConcurrentDictionary<string, List<uint>>();
        var allWorkersSubmittedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        using var pool = new MiningWorkerPool(
            new TestMiningAlgorithm(),
            workerCount,
            submitShare: (share, _) =>
            {
                var nonce = BinaryPrimitives.ReadUInt32LittleEndian(Convert.FromHexString(share.Nonce));
                var nonces = submitted.GetOrAdd(share.WorkerName, _ => []);
                lock (nonces)
                {
                    nonces.Add(nonce);
                    if (submitted.Count == workerCount && submitted.All(pair => pair.Value.Count >= 2))
                    {
                        allWorkersSubmittedTwice.TrySetResult();
                    }
                }

                return Task.CompletedTask;
            },
            algorithmFactory: () =>
            {
                Interlocked.Increment(ref created);
                return new CoordinatedMiningAlgorithm(state);
            });

        await pool.StartAsync(CancellationToken.None);
        pool.AssignJob(new PoolJob
        {
            JobId = "job-1",
            Blob = new string('0', 86),
            Target = "ffffffff",
            Difficulty = 1
        });

        await allWorkersSubmittedTwice.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(workerCount, created);
        Assert.Equal(workerCount, state.InitializedAlgorithms);
        Assert.Equal(workerCount, state.HashThreadIds.Count);
        foreach (var (workerName, nonces) in submitted)
        {
            lock (nonces)
            {
                Assert.True(nonces.Count >= 2);
                Assert.Equal(uint.Parse(workerName.AsSpan("vanta-".Length)), nonces[0]);
                Assert.Equal((uint)workerCount, unchecked(nonces[1] - nonces[0]));
            }
        }
    }

    private sealed class TestMiningAlgorithm : IMiningAlgorithm
    {
        public string Name => "Test";

        public void Initialize(ReadOnlySpan<byte> seedHash) { }

        public void Hash(ReadOnlySpan<byte> input, Span<byte> output)
        {
            output.Clear();
            output[0] = 1;
        }

        public void Dispose() { }
    }

    private sealed class FixedHashAlgorithm(ulong value) : IMiningAlgorithm
    {
        public string Name => "Test";

        public void Initialize(ReadOnlySpan<byte> seedHash) { }

        public void Hash(ReadOnlySpan<byte> input, Span<byte> output)
        {
            output.Clear();
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(output, value);
        }

        public void Dispose() { }
    }

    private sealed class CoordinatedMiningAlgorithm(WorkerState state) : IMiningAlgorithm
    {
        private int _firstHash = 1;

        public string Name => "Coordinated mining test algorithm";

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

            state.HashThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            state.FirstHashes.Signal();
            if (!state.FirstHashes.Wait(TimeSpan.FromSeconds(2)))
            {
                throw new TimeoutException("Not all mining workers entered Hash().");
            }
        }

        public void Dispose() { }
    }

    private sealed class WorkerState(int workers)
    {
        public CountdownEvent FirstHashes { get; } = new(workers);
        public ConcurrentDictionary<int, byte> HashThreadIds { get; } = new();
        public int InitializedAlgorithms;
    }
}
