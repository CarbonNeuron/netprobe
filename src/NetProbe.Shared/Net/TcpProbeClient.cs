using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetProbe.Shared.Protocol;
using NetProbe.Shared.Stats;

namespace NetProbe.Shared.Net;

/// <summary>
/// TCP probe client using connection-per-probe. Each probe opens a new TCP connection,
/// sends a length-prefixed Probe, reads the Echo, and closes. RTT includes connection setup.
/// </summary>
public sealed class TcpProbeClient
{
    private readonly IPAddress _serverAddress;
    private readonly int _serverPort;

    public TcpProbeClient(IPAddress serverAddress, int serverPort)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }

    /// <summary>
    /// Runs the probe test and returns a StatsCollector with all results.
    /// </summary>
    public Task<StatsCollector> RunAsync(int count, int intervalMs, int payloadSize, int timeoutSeconds, CancellationToken ct)
        => RunAsync(count, intervalMs, payloadSize, timeoutSeconds, ct, collector: null);

    /// <summary>
    /// Runs the probe test using an externally provided StatsCollector (for live dashboard polling).
    /// </summary>
    public async Task<StatsCollector> RunAsync(int count, int intervalMs, int payloadSize, int timeoutSeconds, CancellationToken ct, StatsCollector? collector)
    {
        collector ??= new StatsCollector(totalSent: count);
        var serverEp = new IPEndPoint(_serverAddress, _serverPort);
        var payload = new byte[payloadSize];

        for (uint seq = 0; seq < count; seq++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var startTimestamp = Stopwatch.GetTimestamp();

                using var socket = new Socket(_serverAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                await socket.ConnectAsync(serverEp, connectCts.Token);

                var probe = new Packet
                {
                    Type = PacketType.Probe,
                    SequenceNumber = seq,
                    Timestamp = startTimestamp,
                    Payload = payload,
                };

                // Send length-prefixed packet
                var probeBytes = new byte[probe.WireSize];
                probe.WriteTo(probeBytes);

                var frameBuf = new byte[4 + probeBytes.Length];
                BinaryPrimitives.WriteUInt32BigEndian(frameBuf, (uint)probeBytes.Length);
                probeBytes.CopyTo(frameBuf.AsSpan(4));

                await socket.SendAsync(frameBuf, SocketFlags.None, connectCts.Token);
                collector.IncrementSent();

                // Read echo
                var lenBuf = new byte[4];
                await ReceiveExactAsync(socket, lenBuf, connectCts.Token);
                var echoLen = (int)BinaryPrimitives.ReadUInt32BigEndian(lenBuf);

                var echoBuf = new byte[echoLen];
                await ReceiveExactAsync(socket, echoBuf, connectCts.Token);

                var echo = Packet.ReadFrom(echoBuf);
                var receiveTimestamp = Stopwatch.GetTimestamp();

                if (echo.Type == PacketType.Echo)
                {
                    var rttMs = (receiveTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
                    collector.RecordResult(new ProbeResult(echo.SequenceNumber, rttMs, echo.Payload.Length));
                }

                try { socket.Shutdown(SocketShutdown.Both); } catch { }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Connection failed — counts as loss
            }

            if (seq < count - 1)
                await Task.Delay(intervalMs, ct);
        }

        return collector;
    }

    private static async Task ReceiveExactAsync(Socket socket, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var received = await socket.ReceiveAsync(buffer.AsMemory(offset), SocketFlags.None, ct);
            if (received == 0) throw new EndOfStreamException("Connection closed before all data received.");
            offset += received;
        }
    }
}
