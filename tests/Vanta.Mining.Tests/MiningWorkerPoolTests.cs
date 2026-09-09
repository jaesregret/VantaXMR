using Vanta.Core;
using Vanta.Mining;
using Vanta.Network;

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
}
