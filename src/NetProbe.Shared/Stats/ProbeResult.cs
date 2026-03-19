namespace NetProbe.Shared.Stats;

/// <summary>
/// Result of a single probe packet round-trip.
/// </summary>
/// <param name="SequenceNumber">Packet sequence number.</param>
/// <param name="RttMs">Round-trip time in milliseconds.</param>
/// <param name="PayloadSize">Payload size in bytes.</param>
public readonly record struct ProbeResult(uint SequenceNumber, double RttMs, int PayloadSize);
