using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetProbe.Shared.Protocol;
using NetProbe.Shared.Stats;

namespace NetProbe.Shared.Net;

/// <summary>
/// UDP MTU prober using binary search on application payload size.
/// </summary>
public sealed class MtuProber
{
    private const int MinPayloadSize = 64;
    private const int MaxPayloadSize = 1472;
    private const int ProbesPerStep = 3;
    private const int SuccessThreshold = 2;
    private const int StepTimeoutSeconds = 2;

    private readonly IPAddress _serverAddress;
    private readonly int _serverPort;

    public MtuProber(IPAddress serverAddress, int serverPort)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }

    /// <summary>
    /// Runs the MTU binary search probe and returns results.
    /// </summary>
    public async Task<MtuProbeResult> ProbeAsync(CancellationToken ct)
    {
        var dontFragmentEnabled = false;

        using var socket = new Socket(_serverAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0)); // needed for ReceiveFromAsync

        // Best-effort DontFragment
        try
        {
            socket.DontFragment = true;
            dontFragmentEnabled = true;
        }
        catch (SocketException) { }

        var serverEp = new IPEndPoint(_serverAddress, _serverPort);

        var maxPayload = await BinarySearchPayloadSizeAsync(MinPayloadSize, MaxPayloadSize,
            size => TestPayloadSizeAsync(socket, serverEp, size, ct));

        var caveat = dontFragmentEnabled
            ? "Estimates assume no IP options, tunneling, or additional encapsulation overhead."
            : "DontFragment could not be enabled on this platform. Results may reflect fragmentation success rather than true path MTU behavior.";

        if (maxPayload < MinPayloadSize)
        {
            return new MtuProbeResult
            {
                MaxApplicationPayload = 0,
                EstimatedMaxUdpPayload = 0,
                EstimatedPathMtu = 0,
                DontFragmentEnabled = dontFragmentEnabled,
                Caveat = "No payload size succeeded. Server may be unreachable.",
            };
        }

        return new MtuProbeResult
        {
            MaxApplicationPayload = maxPayload,
            EstimatedMaxUdpPayload = maxPayload + 24, // + protocol header
            EstimatedPathMtu = maxPayload + 24 + 28,  // + IP(20) + UDP(8)
            DontFragmentEnabled = dontFragmentEnabled,
            Caveat = caveat,
        };
    }

    private async Task<bool> TestPayloadSizeAsync(Socket socket, IPEndPoint serverEp, int payloadSize, CancellationToken ct)
    {
        var payload = new byte[payloadSize];
        var successCount = 0;

        for (var i = 0; i < ProbesPerStep; i++)
        {
            try
            {
                var probe = new Packet
                {
                    Type = PacketType.Probe,
                    SequenceNumber = (uint)i,
                    Timestamp = Stopwatch.GetTimestamp(),
                    Payload = payload,
                };

                var buffer = new byte[probe.WireSize];
                probe.WriteTo(buffer);

                await socket.SendToAsync(buffer, SocketFlags.None, serverEp, ct);

                using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stepCts.CancelAfter(TimeSpan.FromSeconds(StepTimeoutSeconds));

                var recvBuf = new byte[65535];
                var remoteEp = new IPEndPoint(IPAddress.Any, 0);

                var result = await socket.ReceiveFromAsync(recvBuf, SocketFlags.None, remoteEp, stepCts.Token);
                var echo = Packet.ReadFrom(recvBuf.AsSpan(0, result.ReceivedBytes));

                if (echo.Type == PacketType.Echo)
                    successCount++;
            }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        return successCount >= SuccessThreshold;
    }

    /// <summary>
    /// Async binary search for the maximum payload size where testFunc returns true.
    /// </summary>
    public static async Task<int> BinarySearchPayloadSizeAsync(int minSize, int maxSize, Func<int, Task<bool>> testFunc)
    {
        var result = minSize - 1;

        while (minSize <= maxSize)
        {
            var mid = minSize + (maxSize - minSize) / 2;

            if (await testFunc(mid))
            {
                result = mid;
                minSize = mid + 1;
            }
            else
            {
                maxSize = mid - 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Synchronous overload for unit testing with deterministic test functions.
    /// </summary>
    public static int BinarySearchPayloadSize(int minSize, int maxSize, Func<int, bool> testFunc)
    {
        var result = minSize - 1;

        while (minSize <= maxSize)
        {
            var mid = minSize + (maxSize - minSize) / 2;

            if (testFunc(mid))
            {
                result = mid;
                minSize = mid + 1;
            }
            else
            {
                maxSize = mid - 1;
            }
        }

        return result;
    }
}
