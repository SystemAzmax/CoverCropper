namespace WebDeza.Imaging.Editing;

/// <summary>名前を付けた書き出し設定</summary>
/// <param name="Name">プリセットの名前</param>
/// <param name="Settings">書き出し設定</param>
/// <param name="IsBuiltIn">組み込みのプリセットか（組み込みは削除できない）</param>
public sealed record ExportPreset(string Name, ExportSettings Settings, bool IsBuiltIn);
