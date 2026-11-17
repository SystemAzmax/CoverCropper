using System.Globalization;

namespace WebDeza.Core;

/// <summary>
/// バイト数の表示（設計書 F-09）。1 KB = 1024 バイト。1 KB 未満は「B」、1 MB 未満は「KB」（小数 1 桁）、
/// それ以上は「MB」（小数 2 桁）。例: 1023 B / 1.0 KB / 182.4 KB / 1.46 MB
/// </summary>
public static class FileSizeFormatter
{
    private const double Kilo = 1024;
    private const double Mega = 1024 * 1024;

    /// <summary>バイト数を表示用の文字にする</summary>
    /// <exception cref="ArgumentOutOfRangeException">bytes が負</exception>
    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes < Kilo) return bytes.ToString(CultureInfo.InvariantCulture) + " B";

        // 丸めると 1024.0 KB になる大きさ（1 MB の直前）は、MB で出す
        double kilobytes = Math.Round(bytes / Kilo, 1, MidpointRounding.AwayFromZero);
        if (kilobytes < Kilo) return kilobytes.ToString("0.0", CultureInfo.InvariantCulture) + " KB";

        double megabytes = Math.Round(bytes / Mega, 2, MidpointRounding.AwayFromZero);
        return megabytes.ToString("0.00", CultureInfo.InvariantCulture) + " MB";
    }
}
