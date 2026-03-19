using System.Net;
using NetProbe.Shared.Net;
using Xunit;

namespace NetProbe.Tests.Integration;

public class TcpRoundtripTests
{
    [Fact]
    public async Task SendAndReceive_AllPacketsEchoed()
    {
        const int port = 0;
        await using var server = new TcpProbeServer(IPAddress.Loopback, port);
        var actualPort = server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new TcpProbeClient(IPAddress.Loopback, actualPort);

        var collector = await client.RunAsync(
            count: 5,
            intervalMs: 10,
            payloadSize: 32,
            timeoutSeconds: 5,
            cts.Token);

        Assert.Equal(5, collector.TotalSent);
        Assert.Equal(5, collector.ReceivedCount);
        Assert.Equal(0.0, collector.LossPercentage);
    }

    [Fact]
    public async Task SendAndReceive_RttIncludesConnectionSetup()
    {
        const int port = 0;
        await using var server = new TcpProbeServer(IPAddress.Loopback, port);
        var actualPort = server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new TcpProbeClient(IPAddress.Loopback, actualPort);

        var collector = await client.RunAsync(
            count: 3,
            intervalMs: 10,
            payloadSize: 64,
            timeoutSeconds: 5,
            cts.Token);

        // RTT should be positive (includes TCP handshake)
        Assert.All(collector.Results, r => Assert.True(r.RttMs > 0));
    }

    [Fact]
    public async Task SendAndReceive_SequenceNumbersPreserved()
    {
        const int port = 0;
        await using var server = new TcpProbeServer(IPAddress.Loopback, port);
        var actualPort = server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new TcpProbeClient(IPAddress.Loopback, actualPort);

        var collector = await client.RunAsync(
            count: 10,
            intervalMs: 5,
            payloadSize: 16,
            timeoutSeconds: 5,
            cts.Token);

        var seqs = collector.Results.Select(r => r.SequenceNumber).OrderBy(s => s).ToArray();
        var expected = Enumerable.Range(0, 10).Select(i => (uint)i).ToArray();
        Assert.Equal(expected, seqs);
    }
}
