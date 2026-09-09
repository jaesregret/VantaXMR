using Vanta.Core;

namespace Vanta.Core.Tests;

public class WalletAddressValidatorTests
{
    [Fact]
    public void ValidPublicMoneroAddress_IsAccepted()
    {
        var wallet = "48" + new string('A', 94);

        Assert.True(WalletAddressValidator.IsValidPublicAddress(wallet));
    }

    [Fact]
    public void PrivateKeyText_IsRejected()
    {
        const string wallet = "private key: 123456";

        Assert.False(WalletAddressValidator.IsValidPublicAddress(wallet));
    }

    [Fact]
    public void SeedPhraseText_IsRejected()
    {
        const string wallet = "seed phrase example banana";

        Assert.False(WalletAddressValidator.IsValidPublicAddress(wallet));
    }
}
