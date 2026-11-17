using WebDeza.Core;

namespace WebDeza.Tests.Core;

/// <summary>バイト数の表示（設計書 F-09、V21。実装計画書 10-1）</summary>
public class FileSizeFormatterTests
{
    [Theory] // V21
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(186_778, "182.4 KB")]
    [InlineData(1_536_000, "1.46 MB")]
    public void Format_DesignExamples(long bytes, string expected)
        => Assert.Equal(expected, FileSizeFormatter.Format(bytes));

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1075, "1.0 KB")]        // 1.0498… KB
    [InlineData(1076, "1.1 KB")]        // 1.0507… KB
    [InlineData(1_048_524, "1023.9 KB")]
    [InlineData(1_048_525, "1.00 MB")]  // 丸めると 1024.0 KB になるので MB で出す
    [InlineData(1_048_576, "1.00 MB")]
    [InlineData(10_485_760, "10.00 MB")]
    [InlineData(5_000_000_000, "4768.37 MB")] // GB にはしない
    public void Format_Boundaries(long bytes, string expected)
        => Assert.Equal(expected, FileSizeFormatter.Format(bytes));

    [Fact]
    public void Format_Negative_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => FileSizeFormatter.Format(-1));
}
