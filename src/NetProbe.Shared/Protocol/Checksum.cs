using System.IO.Hashing;

namespace NetProbe.Shared.Protocol;

/// <summary>
/// CRC32 checksum computation for packet integrity.
/// </summary>
public static class Checksum
{
    /// <summary>
    /// Computes the CRC32 checksum of the given data.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Crc32.HashToUInt32(data);
    }
}
