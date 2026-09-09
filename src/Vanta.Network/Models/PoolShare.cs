namespace Vanta.Network;

public sealed record PoolShare
{
    public required string JobId { get; init; }
    public required string WorkerName { get; init; }
    public required string Nonce { get; init; }
    public required string HashHex { get; init; }
}
