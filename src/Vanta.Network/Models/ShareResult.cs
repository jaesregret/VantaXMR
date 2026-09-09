namespace Vanta.Network;

public sealed record ShareResult
{
    public required bool Accepted { get; init; }
    public string? Error { get; init; }
}
