using NetProbe.Shared.Protocol;
using Xunit;

namespace NetProbe.Tests.Protocol;

public class ChecksumTests
{
    [Fact]
    public void Compute_EmptySpan_ReturnsZero()
    {
        var result = Checksum.Compute(ReadOnlySpan<byte>.Empty);
        Assert.Equal(0u, result);
    }

    [Fact]
    public void Compute_KnownValue_MatchesCrc32()
    {
        // CRC32 of ASCII "123456789" is 0xCBF43926
        var data = "123456789"u8;
        var result = Checksum.Compute(data);
        Assert.Equal(0xCBF43926u, result);
    }

    [Fact]
    public void Compute_DifferentInputs_ProduceDifferentChecksums()
    {
        var a = Checksum.Compute("hello"u8);
        var b = Checksum.Compute("world"u8);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_SameInput_ProducesSameChecksum()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var a = Checksum.Compute(data);
        var b = Checksum.Compute(data);
        Assert.Equal(a, b);
    }
}
