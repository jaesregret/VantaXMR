using System.Text.Json;
using Vanta.Core;

namespace Vanta.Configuration;

public static class ConfigurationLoader
{
    public static MiningConfiguration Load(string? configPath = null, IEnumerable<string>? cliOverrides = null)
    {
        var configuration = new MiningConfiguration();
        var resolvedPath = ResolveConfigPath(configPath);

        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            configuration = ReadFromFile(resolvedPath);
            configuration.ConfigPath = resolvedPath;
        }
        else
        {
            configuration.ConfigPath = resolvedPath;
        }

        if (cliOverrides is not null)
        {
            ApplyCliOverrides(configuration, cliOverrides);
        }

        return configuration;
    }

    public static string? ResolveConfigPath(string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return configPath;
        }

        var candidatePaths = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "config", "config.json"),
            Path.Combine(AppContext.BaseDirectory, "config", "config.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "config.example.json")
        };

        foreach (var candidate in candidatePaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static MiningConfiguration ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var config = new MiningConfiguration
        {
            Coin = GetString(root, "Mining", "Coin") ?? "XMR",
            Algorithm = GetString(root, "Mining", "Algorithm") ?? "RandomX",
            WalletAddress = GetString(root, "Mining", "WalletAddress") ?? string.Empty,
            Threads = GetInt(root, "Mining", "Threads") ?? 0,
            DonateLevel = GetInt(root, "Mining", "DonateLevel") ?? 0,
            Pool = new PoolConfiguration
            {
                Host = GetString(root, "Mining", "Pool", "Host") ?? string.Empty,
                Port = GetInt(root, "Mining", "Pool", "Port") ?? 0,
                Tls = GetBool(root, "Mining", "Pool", "Tls") ?? true
            }
        };

        config.ConfigPath = path;
        return config;
    }

    public static void ApplyCliOverrides(MiningConfiguration configuration, IEnumerable<string> cliOverrides)
    {
        var list = cliOverrides.Where(static item => !string.IsNullOrWhiteSpace(item)).ToArray();
        for (var index = 0; index < list.Length; index++)
        {
            var arg = list[index];
            switch (arg)
            {
                case "--config":
                    if (index + 1 < list.Length)
                    {
                        configuration.ConfigPath = list[++index];
                    }
                    break;
                case "--wallet":
                    if (index + 1 < list.Length)
                    {
                        configuration.WalletAddress = list[++index];
                    }
                    break;
                case "--pool":
                    if (index + 1 < list.Length)
                    {
                        var poolValue = list[++index];
                        var hostPort = poolValue.Split(':', 2);
                        configuration.Pool.Host = hostPort[0];
                        if (hostPort.Length > 1 && int.TryParse(hostPort[1], out var parsedPoolPort))
                        {
                            configuration.Pool.Port = parsedPoolPort;
                        }
                    }
                    break;
                case "--host":
                    if (index + 1 < list.Length)
                    {
                        configuration.Pool.Host = list[++index];
                    }
                    break;
                case "--port":
                    if (index + 1 < list.Length && int.TryParse(list[++index], out var parsedPort))
                    {
                        configuration.Pool.Port = parsedPort;
                    }
                    break;
                case "--threads":
                    if (index + 1 < list.Length && int.TryParse(list[++index], out var threads))
                    {
                        configuration.Threads = threads;
                    }
                    break;
                case "--tls":
                    if (index + 1 < list.Length && bool.TryParse(list[++index], out var tls))
                    {
                        configuration.Pool.Tls = tls;
                    }
                    break;
                case "--coin":
                    if (index + 1 < list.Length)
                    {
                        configuration.Coin = list[++index];
                    }
                    break;
                case "--algorithm":
                    if (index + 1 < list.Length)
                    {
                        configuration.Algorithm = list[++index];
                    }
                    break;
                case "--donate":
                    if (index + 1 < list.Length && int.TryParse(list[++index], out var donateLevel))
                    {
                        configuration.DonateLevel = donateLevel;
                    }
                    break;
            }
        }
    }

    public static string ExampleJson => """
{
  "Mining": {
    "Coin": "XMR",
    "Algorithm": "RandomX",
    "WalletAddress": "",
    "Pool": {
      "Host": "",
      "Port": 0,
      "Tls": true
    },
    "Threads": 0,
    "DonateLevel": 0
  }
}
""";

    private static string? GetString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out var property))
            {
                return null;
            }

            current = property;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static int? GetInt(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out var property))
            {
                return null;
            }

            current = property;
        }

        return current.ValueKind == JsonValueKind.Number && current.TryGetInt32(out var value) ? value : null;
    }

    private static bool? GetBool(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out var property))
            {
                return null;
            }

            current = property;
        }

        return current.ValueKind == JsonValueKind.True || current.ValueKind == JsonValueKind.False ? current.GetBoolean() : null;
    }
}
