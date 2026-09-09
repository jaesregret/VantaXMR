namespace Vanta.Core;

public sealed class MiningStatistics
{
    private readonly object _sync = new();
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    private long _totalHashes;
    private long _acceptedShares;
    private long _rejectedShares;
    private int _activeWorkers;
    private DateTimeOffset? _lastShareTime;
    private double _currentHashrate;

    public double CurrentHashrate
    {
        get
        {
            lock (_sync)
            {
                return _currentHashrate;
            }
        }
        private set
        {
            lock (_sync)
            {
                _currentHashrate = value;
            }
        }
    }

    public double AverageHashrate => HashrateCalculator.Calculate(_totalHashes, Uptime);

    public long AcceptedShares
    {
        get
        {
            lock (_sync)
            {
                return _acceptedShares;
            }
        }
    }

    public long RejectedShares
    {
        get
        {
            lock (_sync)
            {
                return _rejectedShares;
            }
        }
    }

    public long TotalHashes
    {
        get
        {
            lock (_sync)
            {
                return _totalHashes;
            }
        }
    }

    public TimeSpan Uptime => DateTimeOffset.UtcNow - _startedAt;

    public int ActiveWorkers
    {
        get
        {
            lock (_sync)
            {
                return _activeWorkers;
            }
        }
        set
        {
            lock (_sync)
            {
                _activeWorkers = value;
            }
        }
    }

    public DateTimeOffset? LastShareTime
    {
        get
        {
            lock (_sync)
            {
                return _lastShareTime;
            }
        }
    }

    public void RecordHashes(long hashes, TimeSpan elapsed)
    {
        if (hashes <= 0)
        {
            return;
        }

        lock (_sync)
        {
            _totalHashes += hashes;
            _currentHashrate = HashrateCalculator.Calculate(hashes, elapsed <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : elapsed);
        }
    }

    public void RecordShare(bool accepted)
    {
        lock (_sync)
        {
            if (accepted)
            {
                _acceptedShares += 1;
            }
            else
            {
                _rejectedShares += 1;
            }

            _lastShareTime = DateTimeOffset.UtcNow;
        }
    }
}
