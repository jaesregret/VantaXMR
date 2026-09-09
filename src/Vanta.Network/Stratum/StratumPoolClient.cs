using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Vanta.Network;

public sealed class StratumPoolClient : IPoolClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _tls;
    private readonly string _walletAddress;
    private readonly Channel<PoolJob> _jobs = Channel.CreateUnbounded<PoolJob>();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<int, TaskCompletionSource<ShareResult>> _shareRequests = new();
    private TcpClient? _tcpClient;
    private Stream? _stream;
    private Task? _readLoopTask;
    private TaskCompletionSource<bool>? _loginCompletion;
    private int _loginRequestId;
    private int _reconnectStarted;
    private int _requestId;
    private bool _connected;
    private long _acceptedShares;
    private long _rejectedShares;

    public StratumPoolClient(string host, int port, bool tls, string walletAddress)
    {
        _host = host;
        _port = port;
        _tls = tls;
        _walletAddress = walletAddress;
    }

    public bool IsConnected
    {
        get { lock (_stateLock) return _connected; }
    }

    public event EventHandler<PoolStatistics>? StatisticsUpdated;
    public event EventHandler<ShareResult>? ShareResultReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_host) || _port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("Pool host and port must be valid.");
        }

        var client = new TcpClient();
        await client.ConnectAsync(_host, _port, cancellationToken);
        Stream stream = client.GetStream();
        if (_tls)
        {
            var ssl = new SslStream(stream, false);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken);
            stream = ssl;
        }

        lock (_stateLock)
        {
            _tcpClient = client;
            _stream = stream;
            _connected = true;
            _loginCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        _readLoopTask = ReadLoopAsync(_disposeCts.Token);
        PublishStatistics();
        _loginRequestId = await SendLoginAsync(cancellationToken);
        await (_loginCompletion?.Task ?? throw new InvalidOperationException("Login was not initialized."))
            .WaitAsync(cancellationToken);
    }

    public async Task<PoolJob?> ReadJobAsync(CancellationToken cancellationToken)
    {
        if (await _jobs.Reader.WaitToReadAsync(cancellationToken))
        {
            return await _jobs.Reader.ReadAsync(cancellationToken);
        }
        return null;
    }

    public async Task SubmitShareAsync(PoolShare share, CancellationToken cancellationToken)
    {
        if (!IsConnected || _stream is null)
        {
            throw new InvalidOperationException("Pool is not connected.");
        }

        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<ShareResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateLock) _shareRequests[id] = completion;

        await SendAsync(new
        {
            id,
            jsonrpc = "2.0",
            method = "submit",
            @params = new
            {
                id = share.WorkerName,
                job_id = share.JobId,
                nonce = share.Nonce,
                result = share.HashHex
            }
        }, cancellationToken);

        ShareResult result;
        try
        {
            result = await completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (_stateLock) _shareRequests.Remove(id);
        }
        ShareResultReceived?.Invoke(this, result);
        if (result.Accepted) Interlocked.Increment(ref _acceptedShares);
        else Interlocked.Increment(ref _rejectedShares);
        PublishStatistics();
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        lock (_stateLock) _connected = false;
        _jobs.Writer.TryComplete();
        _writeLock.Dispose();
        PublishStatistics();
    }

    private async Task<int> SendLoginAsync(CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        Volatile.Write(ref _loginRequestId, id);
        await SendAsync(new
        {
            id,
            jsonrpc = "2.0",
            method = "login",
            @params = new
            {
                login = _walletAddress,
                pass = "x",
                agent = "vanta/1.0.0"
            }
        }, cancellationToken);
        return id;
    }

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        if (_stream is null) throw new InvalidOperationException("Pool stream is unavailable.");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload) + "\n");
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stream.WriteAsync(bytes, cancellationToken);
            await _stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null) return;
        using var reader = new StreamReader(_stream, Encoding.UTF8, leaveOpen: true);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                var loginResponseId = StratumMessageParser.GetResponseId(line);
                string? loginError = null;
                var loginSucceeded = loginResponseId == Volatile.Read(ref _loginRequestId) &&
                    StratumMessageParser.IsLoginSuccess(line, out loginError);
                if (loginSucceeded)
                {
                    _loginCompletion?.TrySetResult(true);
                }
                else if (loginResponseId == Volatile.Read(ref _loginRequestId) && loginError is not null)
                {
                    _loginCompletion?.TrySetException(new InvalidOperationException($"Pool login failed: {loginError}"));
                }

                if (StratumMessageParser.TryParseJob(line, out var job) && job is not null)
                {
                    await _jobs.Writer.WriteAsync(job, cancellationToken);
                }

                if (StratumMessageParser.TryParseShareResult(line, out var shareResult, out var responseId) &&
                    responseId is int id)
                {
                    lock (_stateLock)
                    {
                        if (_shareRequests.Remove(id, out var completion))
                        {
                            completion.TrySetResult(shareResult!);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException ex)
        {
            _loginCompletion?.TrySetException(ex);
        }
        finally
        {
            lock (_stateLock)
            {
                _connected = false;
                foreach (var completion in _shareRequests.Values)
                {
                    completion.TrySetResult(new ShareResult { Accepted = false, Error = "Pool connection closed." });
                }
                _shareRequests.Clear();
            }
            _loginCompletion?.TrySetException(new IOException("Pool connection closed."));
            PublishStatistics();
            StartReconnectIfNeeded();
        }
    }

    private void StartReconnectIfNeeded()
    {
        if (_disposeCts.IsCancellationRequested || Interlocked.CompareExchange(ref _reconnectStarted, 1, 0) != 0)
        {
            return;
        }

        _ = ReconnectLoopAsync();
    }

    private async Task ReconnectLoopAsync()
    {
        try
        {
            while (!_disposeCts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _disposeCts.Token);
                try
                {
                    await ConnectAsync(_disposeCts.Token);
                    return;
                }
                catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectStarted, 0);
        }
    }

    private void PublishStatistics() =>
        StatisticsUpdated?.Invoke(this, new PoolStatistics
        {
            Host = _host,
            Port = _port,
            Tls = _tls,
            Connected = IsConnected,
            AcceptedShares = Interlocked.Read(ref _acceptedShares),
            RejectedShares = Interlocked.Read(ref _rejectedShares)
        });
}
