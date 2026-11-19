using System.Runtime.InteropServices;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>Win32 の呼び出し</summary>
internal static class Native
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>そのウィンドウのハンドルが、まだあるか（閉じたウィンドウは false）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    /// <summary>モーダルのダイアログを開いている間、メインウィンドウは無効になる（UIA の IsEnabled には出ない）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    public const uint GaRoot = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    /// <summary>今のカーソルの形（D&D 中のカーソルが、受け付けるときと受け付けないときで違うことを確かめる）</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorInfo(ref CURSORINFO info);

    /// <summary>システムのカーソル（例: 32514 = 待機 IDC_WAIT、32650 = 作業中 IDC_APPSTARTING）</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr instance, int cursorId);

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO
    {
        public int Size;
        public int Flags;
        public IntPtr Cursor;
        public POINT Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    public const uint InputKeyboard = 1;
    public const uint KeyEventExtendedKey = 0x0001;
    public const uint KeyEventKeyUp = 0x0002;

    /// <summary>キーやマウスの入力を送る</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public InputUnion Union;
    }

    /// <summary>INPUT の中身（大きさは、いちばん大きい MOUSEINPUT に合わせる）</summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

/// <summary>
/// 座標を物理ピクセルでそろえるため、UI を操作するスレッドを Per-Monitor V2 にする。
/// UIA の座標・モニターの範囲・画面のキャプチャ・マウスの位置は、呼び出したスレッドの DPI の扱いで変わる
/// </summary>
internal static class Dpi
{
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);
    private const int DpiAwarenessPerMonitorAware = 2;

    public static void EnsureThread()
    {
        if (Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) == DpiAwarenessPerMonitorAware) return;
        Native.SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        if (Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext()) != DpiAwarenessPerMonitorAware)
            throw new InvalidOperationException("スレッドを Per-Monitor の DPI の扱いにできない");
    }
}
