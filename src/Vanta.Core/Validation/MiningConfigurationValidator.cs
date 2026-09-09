namespace Vanta.Core;

public static class MiningConfigurationValidator
{
    public static void Validate(MiningConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!string.Equals(configuration.Coin, "XMR", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Vanta currently supports only XMR coin for mining.");
        }

        if (!string.Equals(configuration.Algorithm, "RandomX", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Vanta currently supports only the RandomX algorithm.");
        }

        if (!WalletAddressValidator.IsValidPublicAddress(configuration.WalletAddress))
        {
            throw new InvalidOperationException("WalletAddress must be a valid public Monero wallet address and must not contain a private key, seed phrase, or mnemonic.");
        }

        if (string.IsNullOrWhiteSpace(configuration.Pool.Host))
        {
            throw new InvalidOperationException("Mining pool host is required.");
        }

        if (configuration.Pool.Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("Mining pool port must be between 1 and 65535.");
        }

        if (configuration.Threads < 0)
        {
            throw new InvalidOperationException("Threads cannot be negative.");
        }

        if (configuration.DonateLevel is < 0 or > 100)
        {
            throw new InvalidOperationException("DonateLevel must be between 0 and 100.");
        }
    }
}
