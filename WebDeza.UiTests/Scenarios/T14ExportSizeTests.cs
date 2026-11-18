using System.Text.RegularExpressions;
using WebDeza.UiTests.Infrastructure;

namespace WebDeza.UiTests.Scenarios;

/// <summary>
/// T14 保存サイズ（設計書 6.4・6.5）。品質のスライダーを動かすと表示が追いかけて変わり、計算中でも保存できる。
/// ツールチップのバイト数と、保存したファイルのバイト数が一致する。batch\photo_01.jpg（4000×3000）を使う
/// </summary>
[Collection(UiCollection.Name)]
public sealed class T14ExportSizeTests(ITestOutputHelper output) : UiTestBase(output)
{
    private static string Photo => UiTestEnvironment.Image("batch", "photo_01.jpg");

    [UiFact]
    [Trait("No", "T14")]
    public void T14_SizeFollowsQualitySliderAndCanSaveWhileComputing()
    {
        var app = OpenInWebDeza(Photo);
        var dialog = ExportDialog.Open(app);
        dialog.WaitSizeReady();
        string text = dialog.FileSizeText(), tip = dialog.Element("FileSizeText").HelpText;
        Check.That("T14 保存サイズに「{サイズ}（元ファイルの {n}%）」、ツールチップにバイト数",
            Regex.IsMatch(text, @"^[\d.]+ (KB|MB|B)（元ファイルの [\d.]+%）$") && Regex.IsMatch(tip, @"^[\d,]+ バイト$"), $"{text} ／ {tip}");

        // スライダーを続けて動かし、その間の表示を読む
        var quality = dialog.Quality;
        var seen = new List<string>();
        foreach (var value in new[] { 40.0, 50, 60, 70, 80 })
        {
            WebDezaApp.SetSlider(quality, value);
            for (int i = 0; i < 4; i++)
            {
                seen.Add(dialog.FileSizeText());
                Thread.Sleep(60);
            }
        }
        dialog.WaitSizeReady();
        long last = dialog.TooltipBytes();
        WebDezaApp.SetSlider(quality, 79);
        dialog.WaitSizeReady();
        WebDezaApp.SetSlider(quality, 80);
        dialog.WaitSizeReady();
        long fresh = dialog.TooltipBytes();
        Check.That("T14 品質のスライダーを動かす → 動かしている間も「計算中…」とサイズが追いかけて変わり、止めると最後の値のサイズ",
            seen.Any(s => s.Contains("計算中")) && seen.Distinct().Count() >= 3 && last == fresh,
            $"途中の表示 {seen.Distinct().Count()} 種（計算中 {seen.Count(s => s.Contains("計算中"))} 回） ／ 止めた後 {last:N0} 計算し直し {fresh:N0}");

        WebDezaApp.SetSlider(quality, 70);
        bool computing = dialog.FileSizeText().Contains("計算中");
        string? file = dialog.SaveNewFile();
        Check.Note($"［保存］を押したとき計算中 {computing}");
        Check.That("T14 計算中に［保存］ → 保存できる", file is not null && Path.GetExtension(file) == ".jpg", Path.GetFileName(file ?? "（無い）"));
        Check.AssertAll();
    }

    [UiFact]
    [Trait("No", "T14")]
    public void T14_TooltipBytesMatchSavedFileForEachFormat()
    {
        var app = OpenInWebDeza(Photo);
        var results = new List<string>();
        bool allMatch = true;
        foreach (var (format, setup) in new (string, Action<ExportDialog>?)[]
                 {
                     ("JPEG", d => WebDezaApp.SetSlider(d.Quality, 85)),
                     ("PNG", null),
                     ("BMP", null),
                     ("TIFF", d => app.Driver.SelectCombo(d.OptionCombo(), "LZW", d.Window)),
                 })
        {
            var dialog = ExportDialog.Open(app);
            dialog.SelectFormat(format);
            setup?.Invoke(dialog);
            dialog.WaitSizeReady(TimeSpan.FromSeconds(60));
            long shown = dialog.TooltipBytes();
            string? file = dialog.SaveNewFile(timeout: TimeSpan.FromSeconds(60));
            long actual = file is null ? -1 : new FileInfo(file).Length;
            allMatch &= shown > 0 && shown == actual;
            results.Add($"{format} {shown:N0} / {actual:N0}");
        }
        Check.That("T14 JPEG（品質 85）・PNG・BMP・TIFF（LZW）で、ツールチップのバイト数と保存したファイルのバイト数が一致", allMatch, string.Join(" ／ ", results));
        Check.AssertAll();
    }
}
