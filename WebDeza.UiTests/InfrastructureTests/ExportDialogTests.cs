using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.InfrastructureTests;

/// <summary>保存のダイアログの操作のうち、画面を動かさないもの（バイト数の表示）</summary>
public class ExportDialogTests
{
    /// <summary>アプリの FileSizeFormatter のテスト（V21 と境目）と同じ例で、同じ表示になる</summary>
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1075, "1.0 KB")]
    [InlineData(1076, "1.1 KB")]
    [InlineData(186_778, "182.4 KB")]
    [InlineData(1_048_524, "1023.9 KB")]
    [InlineData(1_048_525, "1.00 MB")]
    [InlineData(1_536_000, "1.46 MB")]
    [InlineData(5_000_000_000, "4768.37 MB")]
    public void FormatSize_SameAsApp(long bytes, string expected) => Assert.Equal(expected, ExportDialog.FormatSize(bytes));
}
