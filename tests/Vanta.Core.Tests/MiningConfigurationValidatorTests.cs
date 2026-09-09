using Vanta.Core;

namespace Vanta.Core.Tests;

public class MiningConfigurationValidatorTests
{
    [Fact]
    public void ValidConfiguration_PassesValidation()
    {
        var config = new MiningConfiguration
        {
            Coin = "XMR",
            Algorithm = "RandomX",
            WalletAddress = "48" + new string('A', 94),
            Pool = new PoolConfiguration { Host = "pool.example.com", Port = 3333, Tls = true },
            Threads = 4,
            DonateLevel = 0
        };

        var exception = Record.Exception(() => MiningConfigurationValidator.Validate(config));

        Assert.Null(exception);
    }

    [Fact]
    public void SeedPhraseConfiguration_Throws()
    {
        var config = new MiningConfiguration
        {
            Coin = "XMR",
            Algorithm = "RandomX",
            WalletAddress = "seed phrase example",
            Pool = new PoolConfiguration { Host = "pool.example.com", Port = 3333, Tls = true },
            Threads = 4,
            DonateLevel = 0
        };

        Assert.Throws<InvalidOperationException>(() => MiningConfigurationValidator.Validate(config));
    }
}
