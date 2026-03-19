using System.Net;
using System.Net.Sockets;
using NetProbe.Shared.Protocol;

namespace NetProbe.Shared.Net;

/// <summary>
/// UDP probe server that echoes Probe packets back as Echo packets.
/// </summary>
public sealed class UdpProbeServer : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly IPAddress _bindAddress;
    private readonly int _requestedPort;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public UdpProbeServer(IPAddress bindAddress, int port)
    {
        _bindAddress = bindAddress;
        _requestedPort = port;
        _socket = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
    }

    /// <summary>
    /// Starts listening. Returns the actual port (useful when port 0 is requested).
    /// </summary>
    public int Start()
    {
        _socket.Bind(new IPEndPoint(_bindAddress, _requestedPort));
        var actualPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;

        _cts = new CancellationTokenSource();
        _listenTask = ListenAsync(_cts.Token);

        return actualPort;
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        var buffer = new byte[65535];
        var remoteEp = new IPEndPoint(_bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, ct);

                var received = buffer.AsSpan(0, result.ReceivedBytes);
                Packet probe;
                try
                {
                    probe = Packet.ReadFrom(received);
                }
                catch (InvalidDataException)
                {
                    continue; // skip invalid packets
                }

                if (probe.Type != PacketType.Probe) continue;

                var echo = probe.ToEcho();
                var sendBuffer = new byte[echo.WireSize];
                echo.WriteTo(sendBuffer);

                await _socket.SendToAsync(sendBuffer, SocketFlags.None, result.RemoteEndPoint, ct);
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

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_listenTask is not null)
        {
            try { await _listenTask; } catch (OperationCanceledException) { }
        }
        _socket.Dispose();
        _cts?.Dispose();
    }
}
