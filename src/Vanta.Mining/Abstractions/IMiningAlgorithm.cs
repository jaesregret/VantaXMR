namespace Vanta.Mining;

public interface IMiningAlgorithm : IDisposable
{
    string Name { get; }

    void Initialize(ReadOnlySpan<byte> seedHash);

    void Hash(ReadOnlySpan<byte> input, Span<byte> output);
}
