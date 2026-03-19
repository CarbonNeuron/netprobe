using NetProbe.UI;
using Xunit;

namespace NetProbe.Tests.UI;

public class ReportRendererTests
{
    [Theory]
    [InlineData(0.5, "500 us")]
    [InlineData(0.001, "1 us")]
    [InlineData(1.5, "1.50 ms")]
    [InlineData(999.99, "999.99 ms")]
    public void FormatMs_FormatsCorrectly(double input, string expected)
    {
        var result = ReportRenderer.FormatMs(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatMs_LargeValue_UsesHumanizer()
    {
        var result = ReportRenderer.FormatMs(65000);
        Assert.Contains("minute", result);
    }
}
