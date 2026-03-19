using System.Net;
using NetProbe.Shared.Net;
using NetProbe.Shared.Stats;
using Xunit;

namespace NetProbe.Tests.Integration;

public class MtuProbeTests
{
    [Fact]
    public async Task BinarySearch_FindsMaxPayload_OnLoopback()
    {
        // On loopback, all sizes should succeed (up to 1472)
        const int port = 0;
        await using var server = new UdpProbeServer(IPAddress.Loopback, port);
        var actualPort = server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var prober = new MtuProber(IPAddress.Loopback, actualPort);

        var result = await prober.ProbeAsync(cts.Token);

        // Loopback supports large payloads, should find max (1472)
        Assert.True(result.MaxApplicationPayload >= 1472,
            $"Expected max payload >= 1472 on loopback, got {result.MaxApplicationPayload}");
    }

    [Fact]
    public async Task BinarySearch_ReportsEstimates()
    {
        const int port = 0;
        await using var server = new UdpProbeServer(IPAddress.Loopback, port);
        var actualPort = server.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var prober = new MtuProber(IPAddress.Loopback, actualPort);

        var result = await prober.ProbeAsync(cts.Token);

        // Estimated UDP payload = app payload + 24 (protocol header)
        Assert.Equal(result.MaxApplicationPayload + 24, result.EstimatedMaxUdpPayload);
        // Estimated path MTU = UDP payload + 28 (IP 20 + UDP 8)
        Assert.Equal(result.EstimatedMaxUdpPayload + 28, result.EstimatedPathMtu);
    }

    [Fact]
    public void BinarySearch_Algorithm_ConvergesCorrectly()
    {
        // Test the binary search logic in isolation with a known threshold
        var threshold = 500;
        var result = MtuProber.BinarySearchPayloadSize(
            minSize: 64,
            maxSize: 1472,
            testFunc: size => size <= threshold);

        Assert.Equal(threshold, result);
    }

    [Fact]
    public void BinarySearch_AllFail_ReturnsMinMinusOne()
    {
        var result = MtuProber.BinarySearchPayloadSize(
            minSize: 64,
            maxSize: 1472,
            testFunc: _ => false);

        Assert.Equal(63, result); // min - 1 indicates total failure
    }

    [Fact]
    public void BinarySearch_AllSucceed_ReturnsMax()
    {
        var result = MtuProber.BinarySearchPayloadSize(
            minSize: 64,
            maxSize: 1472,
            testFunc: _ => true);

        Assert.Equal(1472, result);
    }
}
