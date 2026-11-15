using System.Runtime.InteropServices;

namespace WebDeza.Services;

/// <summary>
/// エクスプローラーと同じ自然順の比較（設計書 5.1）。数字の並びを数として比べる（a1 &lt; a2 &lt; a10）。大文字小文字は区別しない。
/// Windows の StrCmpLogicalW（shlwapi.dll）を呼ぶ。
/// </summary>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        return NativeMethods.StrCmpLogicalW(x, y);
    }

    private static class NativeMethods
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);
    }
}
