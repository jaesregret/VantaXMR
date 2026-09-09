namespace Vanta.Network;

public sealed record PoolStatistics
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool Tls { get; init; }
    public required bool Connected { get; init; }
    public long AcceptedShares { get; init; }
    public long RejectedShares { get; init; }
    public string PoolName => Tls ? $"{Host}:{Port} (TLS)" : $"{Host}:{Port}";
}
