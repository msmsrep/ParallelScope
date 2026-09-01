using System.Globalization;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

public class FileSizeFormatterTests
{
    public FileSizeFormatterTests()
    {
        // 小数点の記号がカルチャ依存（"1.5" / "1,5"）になるため、期待値を固定できるよう揃える
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    public void Format_UsesLargestUnitThatKeepsValueAtLeastOne(long bytes, string expected)
    {
        Assert.Equal(expected, FileSizeFormatter.Format(bytes));
    }

    [Fact]
    public void Format_RoundsToTwoDecimalPlaces()
    {
        // 1500000 / 1024 / 1024 = 1.4305... → 小数第2位まで
        Assert.Equal("1.43 MB", FileSizeFormatter.Format(1_500_000L));
    }

    [Fact]
    public void Format_StopsAtTerabyteForVeryLargeValues()
    {
        // 単位表はTBまでしか無いので、それ以上は桁の大きいTB表記になる
        Assert.Equal("1024 TB", FileSizeFormatter.Format(1024L * 1024 * 1024 * 1024 * 1024));
    }
}
