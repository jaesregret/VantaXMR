namespace Vanta.Network;

public interface IPoolClient : IDisposable
{
    bool IsConnected { get; }

    event EventHandler<PoolStatistics>? StatisticsUpdated;
    event EventHandler<ShareResult>? ShareResultReceived;

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<PoolJob?> ReadJobAsync(CancellationToken cancellationToken);

    Task SubmitShareAsync(PoolShare share, CancellationToken cancellationToken);
}
