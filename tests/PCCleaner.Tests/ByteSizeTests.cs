using System.Globalization;
using PCCleaner.Services;

namespace PCCleaner.Tests;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(-5, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10 * 1024 + 512, "10.5 KB")]
    [InlineData(150L * 1024 * 1024, "150 MB")]
    [InlineData(1288490189L, "1.2 GB")]
    [InlineData(5L * 1024 * 1024 * 1024 * 1024, "5 TB")]
    public void Format_UsesHumanReadableUnits(long bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, ByteSize.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
