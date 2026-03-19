using System.Buffers.Binary;

namespace NetProbe.Shared.Protocol;

/// <summary>
/// Binary packet format for NetProbe protocol.
/// Wire format: [Magic 2B][Version 1B][Type 1B][SeqNum 4B][Timestamp 8B][PayloadLen 4B][Payload NB][CRC32 4B]
/// </summary>
public readonly struct Packet
{
    private const ushort Magic = 0x4E50; // "NP"
    private const byte Version = 0x01;
    private const int HeaderSize = 20; // 2 + 1 + 1 + 4 + 8 + 4
    private const int ChecksumSize = 4;

    public required PacketType Type { get; init; }
    public required uint SequenceNumber { get; init; }
    public required long Timestamp { get; init; }
    public required byte[] Payload { get; init; }

    /// <summary>Total bytes on the wire: header + payload + checksum.</summary>
    public int WireSize => HeaderSize + Payload.Length + ChecksumSize;

    /// <summary>
    /// Serializes this packet into the given buffer (must be at least WireSize bytes).
    /// </summary>
    public void WriteTo(Span<byte> buffer)
    {
        BinaryPrimitives.WriteUInt16BigEndian(buffer[0..], Magic);
        buffer[2] = Version;
        buffer[3] = (byte)Type;
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], SequenceNumber);
        BinaryPrimitives.WriteInt64BigEndian(buffer[8..], Timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[16..], (uint)Payload.Length);
        Payload.CopyTo(buffer[HeaderSize..]);

        var dataSpan = buffer[..(HeaderSize + Payload.Length)];
        var checksum = Checksum.Compute(dataSpan);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[(HeaderSize + Payload.Length)..], checksum);
    }

    /// <summary>
    /// Deserializes a packet from the given buffer. Validates magic bytes and checksum.
    /// </summary>
    public static Packet ReadFrom(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize + ChecksumSize)
            throw new InvalidDataException("Buffer too small for packet header.");

        var magic = BinaryPrimitives.ReadUInt16BigEndian(buffer[0..]);
        if (magic != Magic)
            throw new InvalidDataException($"Invalid magic bytes: 0x{magic:X4}");

        var version = buffer[2];
        if (version != Version)
            throw new InvalidDataException($"Unsupported version: {version}");

        var type = (PacketType)buffer[3];
        var sequenceNumber = BinaryPrimitives.ReadUInt32BigEndian(buffer[4..]);
        var timestamp = BinaryPrimitives.ReadInt64BigEndian(buffer[8..]);
        var payloadLength = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer[16..]);

        if (buffer.Length < HeaderSize + payloadLength + ChecksumSize)
            throw new InvalidDataException("Buffer too small for declared payload.");

        var payload = buffer.Slice(HeaderSize, payloadLength).ToArray();

        var dataSpan = buffer[..(HeaderSize + payloadLength)];
        var expectedChecksum = Checksum.Compute(dataSpan);
        var actualChecksum = BinaryPrimitives.ReadUInt32BigEndian(buffer[(HeaderSize + payloadLength)..]);
        if (expectedChecksum != actualChecksum)
            throw new InvalidDataException("Checksum mismatch.");

        return new Packet
        {
            Type = type,
            SequenceNumber = sequenceNumber,
            Timestamp = timestamp,
            Payload = payload,
        };
    }

    /// <summary>
    /// Creates an Echo packet from this Probe, preserving sequence number and timestamp.
    /// </summary>
    public Packet ToEcho() => new()
    {
        Type = PacketType.Echo,
        SequenceNumber = SequenceNumber,
        Timestamp = Timestamp,
        Payload = Payload,
    };
}
