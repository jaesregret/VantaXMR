using System.Text.RegularExpressions;

namespace Vanta.Core;

public static partial class WalletAddressValidator
{
    private static readonly Regex MoneroPattern = MoneroAddressRegex();
    private static readonly Regex SensitivePattern = new(
        @"(?i)(seed phrase|seedphrase|private key|mnemonic|privkey|passphrase)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidPublicAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (SensitivePattern.IsMatch(candidate))
        {
            return false;
        }

        return MoneroPattern.IsMatch(candidate);
    }

    public static string Redact(string? walletAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return "<not configured>";
        }

        var candidate = walletAddress.Trim();
        if (candidate.Length <= 10)
        {
            return candidate;
        }

        return $"{candidate[..4]}...{candidate[^4..]}";
    }

    [GeneratedRegex("^(?:4|8)[1-9A-Za-z]{94,106}$", RegexOptions.CultureInvariant)]
    private static partial Regex MoneroAddressRegex();
}
