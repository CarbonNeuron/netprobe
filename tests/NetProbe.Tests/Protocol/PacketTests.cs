using NetProbe.Shared.Protocol;
using Xunit;

namespace NetProbe.Tests.Protocol;

public class PacketTests
{
    [Fact]
    public void Roundtrip_ProbePacket_PreservesAllFields()
    {
        var original = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 42,
            Timestamp = 123456789L,
            Payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
        };

        var buffer = new byte[original.WireSize];
        original.WriteTo(buffer);

        var parsed = Packet.ReadFrom(buffer);

        Assert.Equal(original.Type, parsed.Type);
        Assert.Equal(original.SequenceNumber, parsed.SequenceNumber);
        Assert.Equal(original.Timestamp, parsed.Timestamp);
        Assert.Equal(original.Payload, parsed.Payload);
    }

    [Fact]
    public void Roundtrip_EchoPacket_PreservesAllFields()
    {
        var original = new Packet
        {
            Type = PacketType.Echo,
            SequenceNumber = 999,
            Timestamp = 987654321L,
            Payload = Array.Empty<byte>(),
        };

        var buffer = new byte[original.WireSize];
        original.WriteTo(buffer);

        var parsed = Packet.ReadFrom(buffer);

        Assert.Equal(PacketType.Echo, parsed.Type);
        Assert.Equal(999u, parsed.SequenceNumber);
        Assert.Equal(987654321L, parsed.Timestamp);
        Assert.Empty(parsed.Payload);
    }

    [Fact]
    public void WriteTo_SetsMagicBytesAndVersion()
    {
        var packet = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 1,
            Timestamp = 0,
            Payload = Array.Empty<byte>(),
        };

        var buffer = new byte[packet.WireSize];
        packet.WriteTo(buffer);

        // Magic bytes: 0x4E50 ("NP") big-endian
        Assert.Equal(0x4E, buffer[0]);
        Assert.Equal(0x50, buffer[1]);
        // Version: 0x01
        Assert.Equal(0x01, buffer[2]);
    }

    [Fact]
    public void WriteTo_ComputesValidChecksum()
    {
        var packet = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 1,
            Timestamp = 100,
            Payload = new byte[] { 1, 2, 3 },
        };

        var buffer = new byte[packet.WireSize];
        packet.WriteTo(buffer);

        // Checksum is last 4 bytes; verify by recomputing over everything before it
        var dataLen = buffer.Length - 4;
        var expected = Checksum.Compute(buffer.AsSpan(0, dataLen));
        var actual = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(dataLen));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReadFrom_CorruptedChecksum_ThrowsInvalidDataException()
    {
        var packet = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 1,
            Timestamp = 0,
            Payload = Array.Empty<byte>(),
        };

        var buffer = new byte[packet.WireSize];
        packet.WriteTo(buffer);

        // Corrupt last byte (checksum)
        buffer[^1] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => Packet.ReadFrom(buffer));
    }

    [Fact]
    public void ReadFrom_BadMagicBytes_ThrowsInvalidDataException()
    {
        var packet = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 1,
            Timestamp = 0,
            Payload = Array.Empty<byte>(),
        };

        var buffer = new byte[packet.WireSize];
        packet.WriteTo(buffer);

        // Corrupt magic bytes
        buffer[0] = 0x00;

        Assert.Throws<InvalidDataException>(() => Packet.ReadFrom(buffer));
    }

    [Fact]
    public void WireSize_EqualsHeaderPlusPayloadPlusChecksum()
    {
        var packet = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 1,
            Timestamp = 0,
            Payload = new byte[64],
        };

        // 20 header + 64 payload + 4 checksum = 88
        Assert.Equal(88, packet.WireSize);
    }

    [Fact]
    public void ToEcho_FlipsTypePreservesSequenceAndTimestamp()
    {
        var probe = new Packet
        {
            Type = PacketType.Probe,
            SequenceNumber = 7,
            Timestamp = 555L,
            Payload = new byte[] { 1, 2 },
        };

        var echo = probe.ToEcho();

        Assert.Equal(PacketType.Echo, echo.Type);
        Assert.Equal(7u, echo.SequenceNumber);
        Assert.Equal(555L, echo.Timestamp);
        Assert.Equal(probe.Payload, echo.Payload);
    }
}
