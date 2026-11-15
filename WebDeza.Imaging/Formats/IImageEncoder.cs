using WebDeza.Imaging.Parameters;

namespace WebDeza.Imaging.Formats;

/// <summary>形式の書き出し</summary>
public interface IImageEncoder
{
    /// <summary>形式ごとのオプション（JPEG の品質など）。画面の入力欄はここから作る</summary>
    IReadOnlyList<ParameterDescriptor> Options { get; }

    /// <summary>ファイルサイズを調整できるオプション（JPEG は "quality"）。調整できない形式は null</summary>
    SizeTuning? SizeTuning { get; }

    /// <summary>
    /// 画像をエンコードして output に書く。メタデータは書き込まない。
    /// 同じ画像・同じオプションなら、毎回同じバイト列になること。
    /// 透過を保てない形式（Capabilities に Alpha が無い）には、不透明にした画像が渡される。
    /// output は呼び出し側が閉じる。
    /// </summary>
    void Encode(ImageBuffer image, ParameterValues options, Stream output, CancellationToken cancellationToken);
}
