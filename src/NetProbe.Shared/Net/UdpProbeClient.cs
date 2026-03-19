using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetProbe.Shared.Protocol;
using NetProbe.Shared.Stats;

namespace NetProbe.Shared.Net;

/// <summary>
/// UDP probe client that sends numbered Probe packets and collects Echo responses.
/// </summary>
public sealed class UdpProbeClient
{
    private readonly IPAddress _serverAddress;
    private readonly int _serverPort;

    public UdpProbeClient(IPAddress serverAddress, int serverPort)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }

    /// <summary>
    /// Runs the probe test and returns a StatsCollector with all results.
    /// </summary>
    public async Task<StatsCollector> RunAsync(int count, int intervalMs, int payloadSize, int timeoutSeconds, CancellationToken ct)
    {
        using var socket = new Socket(_serverAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        // Bind to an OS-assigned ephemeral port so ReceiveFromAsync works.
        var anyAddress = _serverAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? IPAddress.IPv6Any : IPAddress.Any;
        socket.Bind(new IPEndPoint(anyAddress, 0));
        var serverEp = new IPEndPoint(_serverAddress, _serverPort);

        var collector = new StatsCollector(totalSent: count);
        var pendingTimestamps = new ConcurrentDictionary<uint, long>();
        var payload = new byte[payloadSize];

        // Start receiver task
        using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var receiveTask = ReceiveAsync(socket, collector, pendingTimestamps, receiveCts.Token);

        // Send probes
        for (uint seq = 0; seq < count; seq++)
        {
            ct.ThrowIfCancellationRequested();

            var timestamp = Stopwatch.GetTimestamp();
            pendingTimestamps[seq] = timestamp;

            var probe = new Packet
            {
                Type = PacketType.Probe,
                SequenceNumber = seq,
                Timestamp = timestamp,
                Payload = payload,
            };

            var buffer = new byte[probe.WireSize];
            probe.WriteTo(buffer);

            await socket.SendToAsync(buffer, SocketFlags.None, serverEp, ct);

            if (seq < count - 1)
                await Task.Delay(intervalMs, ct);
        }

        // Wait for trailing responses
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct);
        }
        catch (OperationCanceledException) { }

        receiveCts.Cancel();
        try { await receiveTask; } catch (OperationCanceledException) { }

        return collector;
    }

    private static async Task ReceiveAsync(
        Socket socket,
        StatsCollector collector,
        ConcurrentDictionary<uint, long> pendingTimestamps,
        CancellationToken ct)
    {
        var buffer = new byte[65535];
        var remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, ct);

                var received = buffer.AsSpan(0, result.ReceivedBytes);
                Packet echo;
                try
                {
                    echo = Packet.ReadFrom(received);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (echo.Type != PacketType.Echo) continue;

                var receiveTimestamp = Stopwatch.GetTimestamp();
                if (pendingTimestamps.TryRemove(echo.SequenceNumber, out var sendTimestamp))
                {
                    var rttMs = (receiveTimestamp - sendTimestamp) * 1000.0 / Stopwatch.Frequency;
                    collector.RecordResult(new ProbeResult(echo.SequenceNumber, rttMs, echo.Payload.Length));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
