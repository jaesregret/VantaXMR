namespace Vanta.Core;

public sealed class PoolConfiguration
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool Tls { get; set; } = true;
}

public sealed class MiningConfiguration
{
    public string Coin { get; set; } = "XMR";
    public string Algorithm { get; set; } = "RandomX";
    public string WalletAddress { get; set; } = string.Empty;
    public PoolConfiguration Pool { get; set; } = new();
    public int Threads { get; set; }
    public int DonateLevel { get; set; }
    public string? ConfigPath { get; set; }
}
