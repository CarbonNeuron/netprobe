namespace NetProbe.Shared.Stats;

/// <summary>
/// Collects probe results and computes running statistics including
/// packet loss, reordering, and RFC 3550 interarrival jitter.
/// </summary>
public sealed class StatsCollector
{
    private readonly List<ProbeResult> _results = [];
    private readonly int _totalSent;
    private uint _highestSeq;
    private int _reorderedCount;
    private double _jitter;
    private double _lastRtt = double.NaN;
    private bool _hasFirstPacket;

    public StatsCollector(int totalSent)
    {
        _totalSent = totalSent;
    }

    public int ReceivedCount => _results.Count;
    public int ReorderedCount => _reorderedCount;
    public double CurrentJitter => _jitter;
    public double LossPercentage => _totalSent == 0 ? 0.0 : (_totalSent - _results.Count) * 100.0 / _totalSent;
    public IReadOnlyList<ProbeResult> Results => _results;
    public int TotalSent => _totalSent;

    /// <summary>
    /// Records a received probe result. Thread-safe is NOT guaranteed — call from a single thread.
    /// </summary>
    public void RecordResult(ProbeResult result)
    {
        _results.Add(result);

        // Reordering detection
        if (_hasFirstPacket)
        {
            if (result.SequenceNumber < _highestSeq)
                _reorderedCount++;
            else
                _highestSeq = result.SequenceNumber;
        }
        else
        {
            _highestSeq = result.SequenceNumber;
            _hasFirstPacket = true;
        }

        // RFC 3550 jitter
        if (!double.IsNaN(_lastRtt))
        {
            var d = Math.Abs(result.RttMs - _lastRtt);
            _jitter += (d - _jitter) / 16.0;
        }

        _lastRtt = result.RttMs;
    }
}
