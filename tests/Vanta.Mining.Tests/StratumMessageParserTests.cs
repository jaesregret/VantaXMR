using Vanta.Network;

namespace Vanta.Mining.Tests;

public class StratumMessageParserTests
{
    [Fact]
    public void ParseJob_MessageWithJobId_IsAccepted()
    {
        const string message = "{\"id\":1,\"jsonrpc\":\"2.0\",\"result\":{\"status\":\"OK\",\"job\":{\"job_id\":\"job-42\",\"blob\":\"0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000\",\"target\":\"ffffffff\",\"difficulty\":10,\"seed_hash\":\"0000000000000000000000000000000000000000000000000000000000000000\"}}}";

        var ok = StratumMessageParser.TryParseJob(message, out var job);

        Assert.True(ok);
        Assert.NotNull(job);
        Assert.Equal("job-42", job!.JobId);
        Assert.Equal(10UL, job.Difficulty);
        Assert.Equal(64, job.SeedHash.Length);
    }

    [Fact]
    public void ParseShare_InvalidPayload_IsRejected()
    {
        const string message = "{\"error\":\"bad job\"}";

        var ok = StratumMessageParser.TryParseShare(message, out var share);

        Assert.False(ok);
        Assert.Null(share);
    }

    [Fact]
    public void ParseShareResponse_RecognizesAcceptedResult()
    {
        const string message = "{\"id\":2,\"jsonrpc\":\"2.0\",\"result\":{\"status\":\"OK\"}}";

        var ok = StratumMessageParser.TryParseShareResult(message, out var result, out var responseId);

        Assert.True(ok);
        Assert.True(result!.Accepted);
        Assert.Equal(2, responseId);
    }

    [Fact]
    public void LoginResponse_RecognizesPoolError()
    {
        const string message = "{\"id\":1,\"error\":{\"code\":-1,\"message\":\"invalid login\"}}";

        var ok = StratumMessageParser.IsLoginSuccess(message, out var error);

        Assert.False(ok);
        Assert.Contains("invalid login", error);
    }

    [Fact]
    public void ParseJob_InvalidBlobHex_IsRejected()
    {
        const string message = "{\"method\":\"job\",\"params\":{\"job_id\":\"job-1\",\"blob\":\"zz\",\"target\":\"ffffffff\"}}";

        Assert.False(StratumMessageParser.TryParseJob(message, out _));
    }

    [Fact]
    public void ParseJob_WithoutRandomXSeed_IsRejected()
    {
        const string message = "{\"method\":\"job\",\"params\":{\"job_id\":\"job-1\",\"blob\":\"00000000000000000000000000000000000000000000000000000000000000000000000000000000000000\",\"target\":\"ffffffff\"}}";

        Assert.False(StratumMessageParser.TryParseJob(message, out _));
    }

    [Fact]
    public void ResponseId_IsReadOnlyFromJsonRpcId()
    {
        Assert.Equal(17, StratumMessageParser.GetResponseId("{\"id\":17,\"result\":{\"status\":\"OK\"}}"));
        Assert.Null(StratumMessageParser.GetResponseId("{\"method\":\"job\"}"));
    }
}
