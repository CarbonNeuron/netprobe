using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using NetProbe.Shared.Protocol;

namespace NetProbe.Shared.Net;

/// <summary>
/// TCP probe server that accepts one connection per probe and echoes back.
/// Uses 4-byte big-endian length prefix framing.
/// </summary>
public sealed class TcpProbeServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly IPAddress _bindAddress;
    private readonly int _requestedPort;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public TcpProbeServer(IPAddress bindAddress, int port)
    {
        _bindAddress = bindAddress;
        _requestedPort = port;
        _listener = new Socket(bindAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    }

    /// <summary>
    /// Starts listening. Returns the actual port.
    /// </summary>
    public int Start()
    {
        _listener.Bind(new IPEndPoint(_bindAddress, _requestedPort));
        _listener.Listen(128);
        var actualPort = ((IPEndPoint)_listener.LocalEndPoint!).Port;

        _cts = new CancellationTokenSource();
        _acceptTask = AcceptLoopAsync(_cts.Token);

        return actualPort;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await _listener.AcceptAsync(ct);
                _ = HandleClientAsync(clientSocket, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private static async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        try
        {
            // Read 4-byte length prefix
            var lenBuf = new byte[4];
            await ReceiveExactAsync(client, lenBuf, ct);
            var packetLen = (int)BinaryPrimitives.ReadUInt32BigEndian(lenBuf);

            // Read packet
            var packetBuf = new byte[packetLen];
            await ReceiveExactAsync(client, packetBuf, ct);

            var probe = Packet.ReadFrom(packetBuf);
            if (probe.Type != PacketType.Probe) return;

            // Echo back
            var echo = probe.ToEcho();
            var echoBytes = new byte[echo.WireSize];
            echo.WriteTo(echoBytes);

            var frameBuf = new byte[4 + echoBytes.Length];
            BinaryPrimitives.WriteUInt32BigEndian(frameBuf, (uint)echoBytes.Length);
            echoBytes.CopyTo(frameBuf.AsSpan(4));

            await client.SendAsync(frameBuf, SocketFlags.None, ct);
        }
        catch (Exception) { /* client disconnect, invalid data, etc. */ }
        finally
        {
            try { client.Shutdown(SocketShutdown.Both); } catch { }
            client.Dispose();
        }
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

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener.Dispose(); // unblocks AcceptAsync
        if (_acceptTask is not null)
        {
            try { await _acceptTask; } catch (Exception) { }
        }
        _cts?.Dispose();
    }
}
