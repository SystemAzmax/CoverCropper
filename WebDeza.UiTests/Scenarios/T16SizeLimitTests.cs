using System.Text.RegularExpressions;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T16 ファイルサイズの上限（設計書 6.4・6.5）。上限で保存したファイルが上限以下になり、探した品質が出る。
/// とても小さい上限で M26。batch\photo_01.jpg（4000×3000。細かい模様が多く、品質でサイズが大きく変わる）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T16SizeLimitTests(ITestOutputHelper output) : UiTestBase(output)
{
    [UiFact]
    [Trait("No", "T16")]
    public void T16_SizeLimit_FindsQualitySavesUnderLimitAndWarnsWhenImpossible()
    {
        var app = OpenInWebDeza(UiTestEnvironment.Image("batch", "photo_01.jpg"));
        var dialog = ExportDialog.Open(app);
        dialog.UseSizeLimit(500);
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        Thread.Sleep(500);
        string quality = dialog.QualityText(), headline = dialog.Headline(), warning = dialog.Text("SizeLimitWarningText");
        bool readOnly = dialog.IsQualityReadOnly();
        long shown = dialog.TooltipBytes();
        Check.That("T16 JPEG で「上限を指定」・500 KB → 品質の入力欄が読み取り専用で、探した品質が出る。見出し「JPEG 品質 {品質} ／ {500 KB 以下}」",
            readOnly && int.TryParse(quality, out int found) && found is > 1 and < 100 && headline.Contains($"JPEG 品質 {quality} ／")
                && shown is > 0 and <= 512_000 && warning.Length == 0,
            $"品質 {quality} 読み取り専用 {readOnly} ／ {headline} ／ {shown:N0} バイト");

        string? file = dialog.SaveNewFile(timeout: TimeSpan.FromSeconds(60));
        long saved = file is null ? -1 : new FileInfo(file).Length;
        Check.That("T16 ［保存］ → ファイルが 512,000 バイト以下（表示と同じ大きさ）",
            saved is > 0 and <= 512_000 && saved == shown, $"{saved:N0} バイト（ツールチップ {shown:N0}）");

        dialog = ExportDialog.Open(app);
        app.Driver.EnterValue(dialog.Element("SizeLimitInput"), "1", dialog.Window);
        dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
        Thread.Sleep(500);
        warning = dialog.Text("SizeLimitWarningText");
        string lowest = dialog.QualityText();
        file = dialog.SaveNewFile(timeout: TimeSpan.FromSeconds(60));
        var m = Regex.Match(warning, @"^品質をいちばん低くしても上限（1 KB）を超えます（(.+)）。$");
        string savedSize = file is null ? "（無い）" : ExportDialog.FormatSize(new FileInfo(file).Length);
        Check.That("T16 上限 1 KB → M26「品質をいちばん低くしても上限（1 KB）を超えます（{サイズ}）。」。［保存］ → いちばん低い品質（1）で、そのサイズで保存",
            m.Success && lowest == "1" && savedSize == m.Groups[1].Value, $"{warning} ／ 品質 {lowest} ／ ファイル {savedSize}");

        dialog = ExportDialog.Open(app);
        dialog.SelectFormat("PNG");
        Check.That("T16 PNG を選ぶ → 「上限を指定」が出ない", !dialog.IsShown("UseSizeLimitRadio"));
        dialog.Cancel();
        Check.AssertAll();
    }
}
