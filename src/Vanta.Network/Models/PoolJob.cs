namespace Vanta.Network;

public sealed record PoolJob
{
    public required string JobId { get; init; }
    public required string Blob { get; init; }
    public required string Target { get; init; }
    public required ulong Difficulty { get; init; }
    public string SeedHash { get; init; } = string.Empty;
    public ulong Height { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
