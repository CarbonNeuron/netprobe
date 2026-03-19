using NetProbe.Shared.Stats;
using Xunit;

namespace NetProbe.Tests.Stats;

public class StatsCollectorTests
{
    [Fact]
    public void RecordResult_TracksPacketCount()
    {
        var collector = new StatsCollector(totalSent: 3);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));
        collector.RecordResult(new ProbeResult(1, 12.0, 64));

        Assert.Equal(2, collector.ReceivedCount);
    }

    [Fact]
    public void RecordResult_DetectsReordering()
    {
        var collector = new StatsCollector(totalSent: 5);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));
        collector.RecordResult(new ProbeResult(2, 11.0, 64));
        collector.RecordResult(new ProbeResult(1, 12.0, 64)); // out of order

        Assert.Equal(1, collector.ReorderedCount);
    }

    [Fact]
    public void RecordResult_NoReordering_WhenInOrder()
    {
        var collector = new StatsCollector(totalSent: 3);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));
        collector.RecordResult(new ProbeResult(1, 11.0, 64));
        collector.RecordResult(new ProbeResult(2, 12.0, 64));

        Assert.Equal(0, collector.ReorderedCount);
    }

    [Fact]
    public void Jitter_Rfc3550_ComputesCorrectly()
    {
        var collector = new StatsCollector(totalSent: 4);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));
        collector.RecordResult(new ProbeResult(1, 12.0, 64));
        collector.RecordResult(new ProbeResult(2, 11.0, 64));
        collector.RecordResult(new ProbeResult(3, 15.0, 64));

        // After packet 1: D=|12-10|=2, J = 0 + (2-0)/16 = 0.125
        // After packet 2: D=|11-12|=1, J = 0.125 + (1-0.125)/16 = 0.179688
        // After packet 3: D=|15-11|=4, J = 0.179688 + (4-0.179688)/16 = 0.418457
        Assert.InRange(collector.CurrentJitter, 0.41, 0.43);
    }

    [Fact]
    public void Jitter_SinglePacket_IsZero()
    {
        var collector = new StatsCollector(totalSent: 1);
        collector.RecordResult(new ProbeResult(0, 10.0, 64));

        Assert.Equal(0.0, collector.CurrentJitter);
    }

    [Fact]
    public void LossPercentage_ComputesCorrectly()
    {
        var collector = new StatsCollector(totalSent: 10);
        for (uint i = 0; i < 7; i++)
            collector.RecordResult(new ProbeResult(i, 10.0, 64));

        Assert.Equal(30.0, collector.LossPercentage);
    }

    [Fact]
    public void LossPercentage_AllReceived_IsZero()
    {
        var collector = new StatsCollector(totalSent: 3);
        for (uint i = 0; i < 3; i++)
            collector.RecordResult(new ProbeResult(i, 10.0, 64));

        Assert.Equal(0.0, collector.LossPercentage);
    }
}
