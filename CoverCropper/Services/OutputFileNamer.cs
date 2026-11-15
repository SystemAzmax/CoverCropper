using System.IO;

namespace CoverCropper.Services;

public interface IOutputFileNamer
{
    /// <summary>元名_yyyyMMdd_HHmmss.jpg、重複時は _2, _3...</summary>
    string CreatePath(string outputFolder, string sourcePath, DateTime now);
}

/// <summary>ファイル名の生成（共通の WebDeza.Core.OutputFileNamer に、拡張子 .jpg を渡す）</summary>
public sealed class OutputFileNamer : IOutputFileNamer
{
    private const string Extension = ".jpg";

    private readonly WebDeza.Core.OutputFileNamer _namer;

    public OutputFileNamer() : this(p => File.Exists(p) || Directory.Exists(p)) { }

    /// <param name="exists">パスが既に使われているかの判定（テスト用に差し替え可能）</param>
    public OutputFileNamer(Func<string, bool> exists) => _namer = new WebDeza.Core.OutputFileNamer(exists);

    public string CreatePath(string outputFolder, string sourcePath, DateTime now)
        => _namer.CreatePath(outputFolder, sourcePath, now, Extension);
}
