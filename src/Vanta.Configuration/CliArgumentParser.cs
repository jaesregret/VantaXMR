namespace Vanta.Configuration;

public sealed class CliArgumentParser
{
    public CliArgumentParser(string[] args)
    {
        RawArguments = args ?? Array.Empty<string>();
        Command = RawArguments.Length == 0 ? "help" : RawArguments[0].Trim();
        Arguments = RawArguments.Length > 1 ? RawArguments[1..] : Array.Empty<string>();
    }

    public string Command { get; }
    public IReadOnlyList<string> Arguments { get; }
    public string[] RawArguments { get; }

    public string? ConfigPath => TryGetValue("--config");

    public string? TryGetValue(string option)
    {
        for (var index = 0; index < Arguments.Count; index++)
        {
            if (string.Equals(Arguments[index], option, StringComparison.OrdinalIgnoreCase) && index + 1 < Arguments.Count)
            {
                return Arguments[index + 1];
            }
        }

        return null;
    }
}
