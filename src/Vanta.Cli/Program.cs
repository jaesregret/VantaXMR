using Vanta.Configuration;
using Vanta.Core;
using Vanta.Infrastructure;
using Vanta.Mining;
using Vanta.Network;

var exitCode = await VantaApplication.RunAsync(args);
return exitCode;

public static class VantaApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parser = new CliArgumentParser(args);

        return parser.Command.ToLowerInvariant() switch
        {
            "start" => await StartCommandAsync(parser),
            "config" => ConfigCommand(),
            "benchmark" => await BenchmarkCommandAsync(parser),
            "status" => StatusCommand(parser),
            "version" => VersionCommand(),
            "help" or "--help" or "-h" => HelpCommand(),
            _ => HelpCommand()
        };
    }

    private static int ConfigCommand()
    {
        Console.WriteLine(ConfigurationLoader.ExampleJson);
        return 0;
    }

    private static int StatusCommand(CliArgumentParser parser)
    {
        var config = ConfigurationLoader.Load(parser.ConfigPath, parser.Arguments);
        if (string.IsNullOrWhiteSpace(config.WalletAddress))
        {
            Console.WriteLine("Status: not configured");
            Console.WriteLine("Wallet: <not configured>");
            Console.WriteLine("Pool: <not configured>");
            return 0;
        }

        Console.WriteLine("Status: ready");
        Console.WriteLine($"Coin: {config.Coin}");
        Console.WriteLine($"Algorithm: {config.Algorithm}");
        Console.WriteLine($"Pool: {config.Pool.Host}:{config.Pool.Port}");
        Console.WriteLine($"Wallet: {WalletAddressValidator.Redact(config.WalletAddress)}");
        return 0;
    }

    private static int VersionCommand()
    {
        Console.WriteLine("Vanta 1.0.0");
        return 0;
    }

    private static async Task<int> StartCommandAsync(CliArgumentParser parser)
    {
        var config = ConfigurationLoader.Load(parser.ConfigPath, parser.Arguments);
        try
        {
            MiningConfigurationValidator.Validate(config);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid configuration: {ex.Message}");
            return 1;
        }

        var finalThreads = config.Threads == 0 ? HardwareDetector.DetectPreferredThreadCount() : config.Threads;
        var statistics = new MiningStatistics();
        var tokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            tokenSource.Cancel();
        };

        Console.WriteLine("VANTA");
        Console.WriteLine("Cryptocurrency CPU Miner");
        Console.WriteLine();
        Console.WriteLine($"Coin:       {config.Coin}");
        Console.WriteLine($"Algorithm:  {config.Algorithm}");
        Console.WriteLine($"Pool:       {config.Pool.Host}:{config.Pool.Port}");
        Console.WriteLine($"Threads:    {finalThreads}");
        Console.WriteLine($"Wallet:     {WalletAddressValidator.Redact(config.WalletAddress)}");
        Console.WriteLine();
        Console.WriteLine("Status: STARTING");

        try
        {
            using var cache = new RandomXCache();
            using var algorithm = new RandomXAlgorithm(cache);
            using var pool = new StratumPoolClient(config.Pool.Host, config.Pool.Port, config.Pool.Tls, config.WalletAddress);
            pool.ShareResultReceived += (_, result) => statistics.RecordShare(result.Accepted);
            using var workerPool = new MiningWorkerPool(
                algorithm,
                finalThreads,
                statistics,
                (share, token) => pool.SubmitShareAsync(share, token),
                () => new RandomXAlgorithm(cache));
            await pool.ConnectAsync(tokenSource.Token);
            await workerPool.StartAsync(tokenSource.Token);
            var jobReader = ReadJobsAsync(pool, workerPool, tokenSource.Token);

            Console.WriteLine("Status: MINING");
            Console.WriteLine("Press Ctrl+C to stop mining immediately.");

            try
            {
                while (!tokenSource.IsCancellationRequested)
                {
                    RenderMiningStatus(config, finalThreads, statistics, tokenSource.IsCancellationRequested);
                    await Task.Delay(1000, tokenSource.Token);
                }
            }
            finally
            {
                tokenSource.Cancel();
                try
                {
                    await jobReader;
                }
                catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.WriteLine("Mining cancelled by user.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mining error: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static async Task ReadJobsAsync(
        StratumPoolClient pool,
        MiningWorkerPool workerPool,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await pool.ReadJobAsync(cancellationToken);
            if (job is not null)
            {
                workerPool.AssignJob(job);
            }
        }
    }

    private static void RenderMiningStatus(
        MiningConfiguration config,
        int threads,
        MiningStatistics statistics,
        bool stopping)
    {
        if (!Console.IsOutputRedirected)
        {
            Console.Clear();
        }
        Console.WriteLine("VANTA");
        Console.WriteLine("Cryptocurrency CPU Miner");
        Console.WriteLine();
        Console.WriteLine($"Coin:       {config.Coin}");
        Console.WriteLine($"Algorithm:  {config.Algorithm}");
        Console.WriteLine($"Pool:       {config.Pool.Host}:{config.Pool.Port}");
        Console.WriteLine($"Threads:    {threads}");
        Console.WriteLine($"Wallet:     {WalletAddressValidator.Redact(config.WalletAddress)}");
        Console.WriteLine();
        Console.WriteLine($"Status:     {(stopping ? "STOPPING" : "MINING")}");
        Console.WriteLine($"Hashes:     {statistics.TotalHashes}");
        Console.WriteLine($"Hashrate:   {statistics.CurrentHashrate:F2} H/s");
        Console.WriteLine($"Accepted:   {statistics.AcceptedShares}");
        Console.WriteLine($"Rejected:   {statistics.RejectedShares}");
        Console.WriteLine($"Uptime:     {statistics.Uptime.ToString(@"hh\:mm\:ss")}");
        Console.WriteLine();
        Console.WriteLine("Ctrl+C to stop mining.");
    }

    private static async Task<int> BenchmarkCommandAsync(CliArgumentParser parser)
    {
        var arguments = parser.Arguments.ToArray();
        var threads = 1;
        var seconds = 5;

        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--threads":
                    if (index + 1 < arguments.Length && int.TryParse(arguments[++index], out var parsedThreads))
                    {
                        threads = Math.Max(1, parsedThreads);
                    }
                    break;
                case "--seconds":
                    if (index + 1 < arguments.Length && int.TryParse(arguments[++index], out var parsedSeconds))
                    {
                        seconds = Math.Max(1, parsedSeconds);
                    }
                    break;
            }
        }

        try
        {
            var result = BenchmarkRunner.Run(threads, TimeSpan.FromSeconds(seconds));
            Console.WriteLine($"Threads:         {result.Threads}");
            Console.WriteLine($"Duration:        {result.Duration.TotalSeconds:F2}s");
            Console.WriteLine($"Hashes:          {result.TotalHashes}");
            Console.WriteLine($"Hashrate:        {result.Hashrate:F2} H/s");
            Console.WriteLine($"Hashrate/thread: {result.HashratePerThread:F2} H/s");
            return 0;
        }
        catch (PlatformNotSupportedException ex)
        {
            Console.Error.WriteLine($"Benchmark cannot run without the official RandomX native library: {ex.Message}");
            return 1;
        }
    }

    private static int HelpCommand()
    {
        Console.WriteLine("VANTA");
        Console.WriteLine("Cryptocurrency CPU Miner");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  vanta start [--config path] [--wallet <wallet>] [--pool host:port] [--threads <n>] [--tls true|false]");
        Console.WriteLine("  vanta config");
        Console.WriteLine("  vanta benchmark [--threads <n>] [--seconds <n>]");
        Console.WriteLine("  vanta status");
        Console.WriteLine("  vanta version");
        return 0;
    }
}
