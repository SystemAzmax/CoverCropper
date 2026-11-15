namespace WebDeza.Models;

/// <summary>一覧の 1 件の状態（設計書 F-02）</summary>
public enum ImageItemStatus
{
    /// <summary>まだ書き出していない</summary>
    NotExported,

    /// <summary>書き出し済み</summary>
    Exported,

    /// <summary>読み込みや書き出しに失敗した（理由は ErrorMessage）</summary>
    Error,
}
