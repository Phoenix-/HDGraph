using System.Globalization;
using HDGraph.Core;

namespace HDGraph.Tests;

public sealed class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 * 1024, "10 MB")]
    [InlineData(123456789, "118 MB")]
    [InlineData(1L << 40, "1 TB")]
    public void FormatUsesBinaryUnitsAndInvariantCulture(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(0.1234, "12.3 %")]
    [InlineData(0.0123, "1.23 %")]
    public void FormatPercentUsesInvariantCulture(double fraction, string expected)
    {
        Assert.Equal(expected, SizeFormatter.FormatPercent(fraction, CultureInfo.InvariantCulture));
    }
}
