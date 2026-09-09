using System.Buffers.Binary;
using Vanta.Core;
using Vanta.Network;

namespace Vanta.Mining;

public sealed class MiningWorkerPool : IDisposable
{
    private readonly List<Thread> _workers = new();
    private readonly IMiningAlgorithm _algorithm;
    private readonly MiningStatistics _statistics;
    private readonly Func<PoolShare, CancellationToken, Task> _submitShare;
    private readonly Func<IMiningAlgorithm>? _algorithmFactory;
    private readonly object _jobLock = new();
    private readonly object _lifecycleLock = new();
    private readonly int _workerCount;
    private PoolJob? _currentJob;
    private CancellationTokenSource? _workerCancellation;
    private ManualResetEventSlim? _startGate;
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
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_workers.Count != 0)
            {
                return Task.CompletedTask;
            }

            _workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _startGate = new ManualResetEventSlim(false);
            using var workersReady = new CountdownEvent(_workerCount);

            for (var index = 0; index < _workerCount; index++)
            {
                var workerIndex = index;
                var worker = new Thread(() => WorkerLoop(workerIndex, _workerCancellation.Token, workersReady, _startGate))
                {
                    IsBackground = true,
                    Name = $"Vanta mining worker {workerIndex}"
                };
                _workers.Add(worker);
                worker.Start();
            }

            try
            {
                workersReady.Wait(cancellationToken);
                _startGate.Set();
            }
            catch
            {
                _workerCancellation.Cancel();
                _startGate.Set();
                JoinWorkers();
                throw;
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            _workerCancellation?.Cancel();
            _startGate?.Set();
        }

        JoinWorkers();
        _workerCancellation?.Dispose();
        _startGate?.Dispose();
        _algorithm.Dispose();
        _statistics.ActiveWorkers = 0;
    }

    private void WorkerLoop(
        int workerIndex,
        CancellationToken cancellationToken,
        CountdownEvent workersReady,
        ManualResetEventSlim startGate)
    {
        PoolJob? localJob = null;
        byte[]? blob = null;
        var hash = new byte[32];
        IMiningAlgorithm? workerAlgorithm = null;
        var ready = false;
        uint nonce = (uint)workerIndex;

        try
        {
            workerAlgorithm = _algorithmFactory?.Invoke() ?? _algorithm;
            workersReady.Signal();
            ready = true;
            startGate.Wait(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                PoolJob? job;
                lock (_jobLock) job = _currentJob;
                if (job is null)
                {
                    cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
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
                _statistics.RecordHashes(1, TimeSpan.Zero);

                if (MeetsTarget(hash, job.Target))
                {
                    var share = new PoolShare
                    {
                        JobId = job.JobId,
                        WorkerName = $"vanta-{workerIndex}",
                        Nonce = Convert.ToHexString(blob.AsSpan(39, 4)).ToLowerInvariant(),
                        HashHex = Convert.ToHexString(hash).ToLowerInvariant()
                    };
                    _submitShare(share, cancellationToken).GetAwaiter().GetResult();
                }

                nonce += (uint)_workerCount;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mining worker {workerIndex} stopped: {ex.Message}");
        }
        finally
        {
            if (!ready)
            {
                workersReady.Signal();
            }

            if (workerAlgorithm is not null && !ReferenceEquals(workerAlgorithm, _algorithm))
            {
                workerAlgorithm.Dispose();
            }
        }
    }

    private void JoinWorkers()
    {
        foreach (var worker in _workers)
        {
            if (worker != Thread.CurrentThread)
            {
                worker.Join();
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
