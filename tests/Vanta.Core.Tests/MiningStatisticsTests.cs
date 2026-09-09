using Vanta.Core;

namespace Vanta.Core.Tests;

public class MiningStatisticsTests
{
    [Fact]
    public void RecordHashes_UpdatesTotalAndCurrentHashrate()
    {
        var stats = new MiningStatistics();

        stats.RecordHashes(100, TimeSpan.FromSeconds(1));

        Assert.Equal(100, stats.TotalHashes);
        Assert.True(stats.CurrentHashrate > 0);
    }

    [Fact]
    public void RecordShare_TracksAcceptedAndRejectedShares()
    {
        var stats = new MiningStatistics();

        stats.RecordShare(true);
        stats.RecordShare(false);

        Assert.Equal(1, stats.AcceptedShares);
        Assert.Equal(1, stats.RejectedShares);
    }

    [Fact]
    public void RecordHashes_CurrentHashrateUsesTheAggregateHashCount()
    {
        var stats = new MiningStatistics();
        Thread.Sleep(20);
        stats.RecordHashes(100, TimeSpan.Zero);
        Thread.Sleep(20);
        stats.RecordHashes(100, TimeSpan.Zero);

        Assert.InRange(
            Math.Abs(stats.CurrentHashrate - stats.AverageHashrate),
            0,
            stats.AverageHashrate * 0.1);
    }
}
