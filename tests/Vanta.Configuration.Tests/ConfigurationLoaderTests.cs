using System.Text;
using Vanta.Configuration;
using Vanta.Core;

namespace Vanta.Configuration.Tests;

public class ConfigurationLoaderTests
{
    [Fact]
    public void JsonConfiguration_LoadsWalletAndPool()
    {
        var path = Path.GetTempFileName();
        var validWallet = "48" + new string('A', 94);
        var json = $$"""
        {
          "Mining": {
            "Coin": "XMR",
            "Algorithm": "RandomX",
            "WalletAddress": "{{validWallet}}",
            "Pool": {
              "Host": "pool.example.com",
              "Port": 3333,
              "Tls": true
            },
            "Threads": 8,
            "DonateLevel": 0
          }
        }
        """;

        File.WriteAllText(path, json, Encoding.UTF8);

        try
        {
            var config = ConfigurationLoader.ReadFromFile(path);

            Assert.Equal("XMR", config.Coin);
            Assert.Equal("RandomX", config.Algorithm);
            Assert.Equal("pool.example.com", config.Pool.Host);
            Assert.Equal(3333, config.Pool.Port);
            Assert.True(config.Pool.Tls);
            Assert.Equal(8, config.Threads);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CliOverrides_OverrideJsonValues()
    {
        var config = new MiningConfiguration
        {
            Coin = "XMR",
            Algorithm = "RandomX",
            WalletAddress = "48" + new string('A', 94),
            Pool = new PoolConfiguration { Host = "old.example.com", Port = 3333, Tls = true },
            Threads = 2,
            DonateLevel = 0
        };

        var validWallet = "48" + new string('A', 94);
        ConfigurationLoader.ApplyCliOverrides(config, new[] { "--wallet", validWallet, "--threads", "6", "--pool", "new.example.com:4444" });

        Assert.Equal("new.example.com", config.Pool.Host);
        Assert.Equal(4444, config.Pool.Port);
        Assert.Equal(6, config.Threads);
    }
}
