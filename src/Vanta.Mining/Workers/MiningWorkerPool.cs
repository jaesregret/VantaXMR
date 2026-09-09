using System.Buffers.Binary;
using System.Diagnostics;
using Vanta.Core;
using Vanta.Network;

namespace Vanta.Mining;

public sealed class MiningWorkerPool : IDisposable
{
    private readonly List<Task> _workers = new();
    private readonly IMiningAlgorithm _algorithm;
    private readonly MiningStatistics _statistics;
    private readonly Func<PoolShare, CancellationToken, Task> _submitShare;
    private readonly Func<IMiningAlgorithm>? _algorithmFactory;
    private readonly object _jobLock = new();
    private readonly int _workerCount;
    private PoolJob? _currentJob;
    private bool _disposed;

    public MiningWorkerPool(
        IMiningAlgorithm algorithm,
        int workerCount,
        MiningStatistics? statistics = null,
        Func<PoolShare, CancellationToken, Task>? submitShare = null,
        Func<IMiningAlgorithm>? algorithmFactory = null)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
        _workerCount = workerCount <= 0 ? 1 : workerCount;
        _statistics = statistics ?? new MiningStatistics();
        _submitShare = submitShare ?? ((_, _) => Task.CompletedTask);
        _algorithmFactory = algorithmFactory;
        _statistics.ActiveWorkers = _workerCount;
    }

    public void AssignJob(PoolJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_jobLock) _currentJob = job;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_workers.Count != 0) return Task.CompletedTask;
        for (var index = 0; index < _workerCount; index++)
        {
            var workerIndex = index;
            _workers.Add(Task.Run(() => WorkerLoopAsync(workerIndex, cancellationToken), cancellationToken));
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _algorithm.Dispose();
    }

    private async Task WorkerLoopAsync(int workerIndex, CancellationToken cancellationToken)
    {
        PoolJob? localJob = null;
        byte[]? blob = null;
        var hash = new byte[32];
        var workerAlgorithm = _algorithmFactory?.Invoke() ?? _algorithm;
        uint nonce = (uint)workerIndex;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PoolJob? job;
                lock (_jobLock) job = _currentJob;
                if (job is null)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                if (!ReferenceEquals(job, localJob))
                {
                    localJob = job;
                    blob = Convert.FromHexString(job.Blob);
                    if (blob.Length < 43) throw new InvalidOperationException("Pool job blob is shorter than the Monero nonce offset.");
                    workerAlgorithm.Initialize(ParseSeedHash(job.SeedHash));
                    nonce = (uint)workerIndex;
                }

                BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(39, 4), nonce);
                workerAlgorithm.Hash(blob, hash);
                _statistics.RecordHashes(1, stopwatch.Elapsed);
                stopwatch.Restart();

                if (MeetsTarget(hash, job.Target))
                {
                    var share = new PoolShare
                    {
                        JobId = job.JobId,
                        WorkerName = $"vanta-{workerIndex}",
                        Nonce = Convert.ToHexString(blob.AsSpan(39, 4)).ToLowerInvariant(),
                        HashHex = Convert.ToHexString(hash).ToLowerInvariant()
                    };
                    await _submitShare(share, cancellationToken);
                }

                nonce++;
            }
        }
        finally
        {
            if (!ReferenceEquals(workerAlgorithm, _algorithm))
            {
                workerAlgorithm.Dispose();
            }
        }
    }

    private static byte[] ParseSeedHash(string seedHash) =>
        string.IsNullOrWhiteSpace(seedHash) ? Array.Empty<byte>() : Convert.FromHexString(seedHash);

    private static bool MeetsTarget(ReadOnlySpan<byte> hash, string targetHex)
    {
        var target = Convert.FromHexString(targetHex);
        if (hash.Length < 8 || target.Length is not (4 or 8)) return false;

        var targetValue = target.Length == 4
            ? ExpandTarget(BinaryPrimitives.ReadUInt32LittleEndian(target))
            : BinaryPrimitives.ReadUInt64LittleEndian(target);
        return targetValue != 0 && BinaryPrimitives.ReadUInt64LittleEndian(hash) <= targetValue;
    }

    private static ulong ExpandTarget(uint target) =>
        target == 0 ? 0 : ulong.MaxValue / (uint.MaxValue / (ulong)target);
}
