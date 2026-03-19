using System.Text.Json.Serialization;

namespace NetProbe.Shared.Stats;

/// <summary>
/// Summary report of a completed probe test run.
/// </summary>
public sealed class TestReport
{
    [JsonPropertyName("total_sent")]
    public int TotalSent { get; init; }

    [JsonPropertyName("total_received")]
    public int TotalReceived { get; init; }

    [JsonPropertyName("loss_percentage")]
    public double LossPercentage { get; init; }

    [JsonPropertyName("min_rtt_ms")]
    public double MinRttMs { get; init; }

    [JsonPropertyName("avg_rtt_ms")]
    public double AvgRttMs { get; init; }

    [JsonPropertyName("max_rtt_ms")]
    public double MaxRttMs { get; init; }

    [JsonPropertyName("p95_rtt_ms")]
    public double P95RttMs { get; init; }

    [JsonPropertyName("p99_rtt_ms")]
    public double P99RttMs { get; init; }

    [JsonPropertyName("jitter_ms")]
    public double JitterMs { get; init; }

    [JsonPropertyName("reordered_count")]
    public int ReorderedCount { get; init; }

    [JsonPropertyName("reordered_percentage")]
    public double ReorderedPercentage { get; init; }

    [JsonPropertyName("throughput_bytes_per_sec")]
    public double ThroughputBytesPerSec { get; init; }

    [JsonPropertyName("mtu_probe_result")]
    public MtuProbeResult? MtuResult { get; init; }

    /// <summary>
    /// Builds a TestReport from a completed StatsCollector.
    /// </summary>
    public static TestReport FromCollector(StatsCollector collector, double elapsedSeconds = 0, MtuProbeResult? mtuResult = null)
    {
        var results = collector.Results;
        if (results.Count == 0)
        {
            return new TestReport
            {
                TotalSent = collector.TotalSent,
                TotalReceived = 0,
                LossPercentage = collector.TotalSent == 0 ? 0 : 100.0,
                JitterMs = 0,
                MtuResult = mtuResult,
            };
        }

        var rtts = results.Select(r => r.RttMs).OrderBy(r => r).ToArray();
        var totalBytes = results.Sum(r => (long)r.PayloadSize);

        return new TestReport
        {
            TotalSent = collector.TotalSent,
            TotalReceived = results.Count,
            LossPercentage = collector.LossPercentage,
            MinRttMs = rtts[0],
            AvgRttMs = rtts.Average(),
            MaxRttMs = rtts[^1],
            P95RttMs = Percentile(rtts, 0.95),
            P99RttMs = Percentile(rtts, 0.99),
            JitterMs = collector.CurrentJitter,
            ReorderedCount = collector.ReorderedCount,
            ReorderedPercentage = results.Count == 0 ? 0 : collector.ReorderedCount * 100.0 / results.Count,
            ThroughputBytesPerSec = elapsedSeconds > 0 ? totalBytes / elapsedSeconds : 0,
            MtuResult = mtuResult,
        };
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var index = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}

/// <summary>
/// Results from MTU probing.
/// </summary>
public sealed class MtuProbeResult
{
    [JsonPropertyName("max_application_payload")]
    public int MaxApplicationPayload { get; init; }

    [JsonPropertyName("estimated_max_udp_payload")]
    public int EstimatedMaxUdpPayload { get; init; }

    [JsonPropertyName("estimated_path_mtu")]
    public int EstimatedPathMtu { get; init; }

    [JsonPropertyName("dont_fragment_enabled")]
    public bool DontFragmentEnabled { get; init; }

    [JsonPropertyName("caveat")]
    public string Caveat { get; init; } = "";
}
