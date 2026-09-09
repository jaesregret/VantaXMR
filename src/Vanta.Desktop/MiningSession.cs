using Vanta.Core;
using Vanta.Mining;
using Vanta.Network;

namespace Vanta.Desktop;

public sealed class MiningSession : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _cancellation;
    private RandomXCache? _cache;
    private RandomXAlgorithm? _algorithm;
    private StratumPoolClient? _pool;
    private MiningWorkerPool? _workers;
    private Task? _jobReader;

    public bool IsRunning { get; private set; }
    public MiningStatistics? Statistics { get; private set; }
    public event EventHandler<ShareResult>? ShareResultReceived;

    public async Task StartAsync(MiningConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("A mineração já está em execução.");
            }

            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Statistics = new MiningStatistics();
            _cache = new RandomXCache();
            _algorithm = new RandomXAlgorithm(_cache);
            _pool = new StratumPoolClient(
                configuration.Pool.Host,
                configuration.Pool.Port,
                configuration.Pool.Tls,
                configuration.WalletAddress);
            _pool.ShareResultReceived += OnShareResultReceived;
            _workers = new MiningWorkerPool(
                _algorithm,
                configuration.Threads,
                Statistics,
                (share, token) => _pool.SubmitShareAsync(share, token),
                () => new RandomXAlgorithm(_cache));

            try
            {
                await _pool.ConnectAsync(_cancellation.Token);
                await _workers.StartAsync(_cancellation.Token);
                _jobReader = ReadJobsAsync(_pool, _workers, _cancellation.Token);
                IsRunning = true;
            }
            catch
            {
                await StopCoreAsync();
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycle.Dispose();
    }

    private async Task StopCoreAsync()
    {
        if (_cancellation is null)
        {
            return;
        }

        _cancellation.Cancel();
        if (_jobReader is not null)
        {
            try
            {
                await _jobReader;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_pool is not null)
        {
            _pool.ShareResultReceived -= OnShareResultReceived;
        }

        _workers?.Dispose();
        _pool?.Dispose();
        _algorithm?.Dispose();
        _cache?.Dispose();
        _cancellation.Dispose();

        _workers = null;
        _pool = null;
        _algorithm = null;
        _cache = null;
        _jobReader = null;
        _cancellation = null;
        IsRunning = false;
    }

    private static async Task ReadJobsAsync(
        StratumPoolClient pool,
        MiningWorkerPool workers,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await pool.ReadJobAsync(cancellationToken);
            if (job is not null)
            {
                workers.AssignJob(job);
            }
        }
    }

    private void OnShareResultReceived(object? sender, ShareResult result) =>
        ShareResultReceived?.Invoke(this, result);
}
