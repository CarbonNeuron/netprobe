using NetProbe.Shared.Stats;
using Xunit;

namespace NetProbe.Tests.Stats;

public class TestReportTests
{
    private static StatsCollector BuildCollector(params double[] rtts)
    {
        var collector = new StatsCollector(totalSent: rtts.Length);
        for (var i = 0; i < rtts.Length; i++)
            collector.RecordResult(new ProbeResult((uint)i, rtts[i], 64));
        return collector;
    }

    [Fact]
    public void FromCollector_ComputesMinAvgMax()
    {
        var collector = BuildCollector(10.0, 20.0, 30.0, 40.0, 50.0);
        var report = TestReport.FromCollector(collector);

        Assert.Equal(10.0, report.MinRttMs);
        Assert.Equal(30.0, report.AvgRttMs);
        Assert.Equal(50.0, report.MaxRttMs);
    }

    [Fact]
    public void FromCollector_ComputesPercentiles()
    {
        // 100 values: 1.0, 2.0, 3.0, ..., 100.0
        var rtts = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        var collector = BuildCollector(rtts);
        var report = TestReport.FromCollector(collector);

        // p95 = value at index 94 (0-indexed) = 95.0
        Assert.Equal(95.0, report.P95RttMs);
        // p99 = value at index 98 = 99.0
        Assert.Equal(99.0, report.P99RttMs);
    }

    [Fact]
    public void FromCollector_SinglePacket_AllPercentilesEqual()
    {
        var collector = BuildCollector(42.0);
        var report = TestReport.FromCollector(collector);

        Assert.Equal(42.0, report.MinRttMs);
        Assert.Equal(42.0, report.AvgRttMs);
        Assert.Equal(42.0, report.MaxRttMs);
        Assert.Equal(42.0, report.P95RttMs);
        Assert.Equal(42.0, report.P99RttMs);
    }

    [Fact]
    public void FromCollector_NoPacketsReceived_ReturnsZeroRtt()
    {
        var collector = new StatsCollector(totalSent: 10);
        var report = TestReport.FromCollector(collector);

        Assert.Equal(0.0, report.MinRttMs);
        Assert.Equal(0.0, report.AvgRttMs);
        Assert.Equal(0.0, report.MaxRttMs);
        Assert.Equal(100.0, report.LossPercentage);
    }

    [Fact]
    public void FromCollector_IncludesLossAndReordering()
    {
        var collector = new StatsCollector(totalSent: 5);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));
        collector.RecordResult(new ProbeResult(2, 12.0, 64));
        collector.RecordResult(new ProbeResult(1, 11.0, 64)); // reordered

        var report = TestReport.FromCollector(collector);

        Assert.Equal(5, report.TotalSent);
        Assert.Equal(3, report.TotalReceived);
        Assert.Equal(40.0, report.LossPercentage);
        Assert.Equal(1, report.ReorderedCount);
    }

    [Fact]
    public void FromCollector_IncludesJitter()
    {
        var collector = BuildCollector(10.0, 20.0, 10.0, 20.0);
        var report = TestReport.FromCollector(collector);

        Assert.True(report.JitterMs > 0);
    }
}
