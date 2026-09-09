using System.Text.Json;
using Vanta.Core;
using Vanta.Infrastructure;

namespace Vanta.Desktop;

public sealed class DesktopSettings
{
    public string WalletAddress { get; init; } = string.Empty;
    public string PoolHost { get; init; } = "pool.supportxmr.com";
    public int Port { get; init; } = 7777;
    public int Threads { get; init; } = HardwareDetector.DetectPreferredThreadCount();
    public bool Tls { get; init; }

    public static DesktopSettings From(MiningConfiguration configuration) => new()
    {
        WalletAddress = configuration.WalletAddress,
        PoolHost = configuration.Pool.Host,
        Port = configuration.Pool.Port,
        Threads = configuration.Threads,
        Tls = configuration.Pool.Tls
    };
}

public sealed class DesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Vanta",
        "settings.json");

    public DesktopSettings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(_path)) ?? new DesktopSettings()
                : new DesktopSettings();
        }
        catch (Exception)
        {
            return new DesktopSettings();
        }
    }

    public void Save(DesktopSettings settings)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Settings directory is unavailable.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
