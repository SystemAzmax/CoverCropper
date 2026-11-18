using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace WebDeza.UiTests.Infrastructure;

/// <summary>
/// アプリ（webDeza・CoverCropper）の exe を起動して、UI Automation（FlaUI の UIA3）で操作する。
/// 操作の注意は設計書 6.5 のとおり（メニューの項目は開いている間だけツリーにある、コンボボックスはマウスで選ぶ、など）
/// </summary>
public sealed class AppDriver : IDisposable
{
    private static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(5);

    private bool disposed;

    private AppDriver(Application application, string appName)
    {
        Application = application;
        AppName = appName;
    }

    public Application Application { get; }

    public UIA3Automation Automation { get; } = new();

    /// <summary>アプリ名（メインウィンドウとメッセージボックスのタイトル）</summary>
    public string AppName { get; }

    public Window Main { get; private set; } = null!;

    public int ProcessId => Application.ProcessId;

    public bool HasExited => Application.HasExited;

    // ------------------------------------------------------------ 起動と後始末

    /// <summary>exe を起動し、メインウィンドウが出るまで待つ</summary>
    public static AppDriver Launch(string exe, string appName, IEnumerable<string> args)
    {
        Dpi.EnsureThread();
        if (!File.Exists(exe)) throw new FileNotFoundException($"{appName} の exe が無い。先に {UiTestEnvironment.Configuration} でビルドする", exe);

        var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var a in args) info.ArgumentList.Add(a);
        var driver = new AppDriver(Application.Launch(info), appName);
        try
        {
            driver.Main = Waits.For(() => driver.TopWindows().FirstOrDefault(w => w.Name.Contains(appName)), TimeSpan.FromSeconds(20), appName + " のメインウィンドウ");
            Thread.Sleep(500);
            return driver;
        }
        catch
        {
            driver.Dispose();
            throw;
        }
    }

    /// <summary>終わるのを待つ（終わったら true）</summary>
    public bool WaitForExit(TimeSpan timeout) => Waits.Until(() => Application.HasExited, timeout);

    /// <summary>まだ動いていれば止める（設定は保存されない）。何度呼んでもよい</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!Application.HasExited)
            {
                Application.Kill();
                WaitForExit(TimeSpan.FromSeconds(5));
            }
        }
        catch (InvalidOperationException) { }
        Automation.Dispose();
        Application.Dispose();
    }

    // ------------------------------------------------------------ ウィンドウと要素

    /// <summary>このプロセスのトップレベルのウィンドウ</summary>
    public IReadOnlyList<Window> TopWindows() => Application.GetAllTopLevelWindows(Automation);

    /// <summary>トップレベルのウィンドウとその中のウィンドウ（所有されたダイアログは、メインの子になることがある）</summary>
    public IReadOnlyList<AutomationElement> AllWindows()
    {
        var list = new List<AutomationElement>();
        foreach (var w in TopWindows())
        {
            list.Add(w);
            list.AddRange(w.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)));
        }
        return list;
    }

    /// <summary>
    /// メインウィンドウが使えるか。モーダルのダイアログを開いている間は使えない。
    /// UIA の IsEnabled には出ないので、Win32 の IsWindowEnabled で調べる
    /// </summary>
    public bool IsMainEnabled() => Native.IsWindowEnabled(Main.Properties.NativeWindowHandle.ValueOrDefault);

    /// <summary>そのウィンドウが、まだ開いているか</summary>
    public bool IsOpen(AutomationElement window) => AllWindows().Any(w => w.Equals(window));

    public static AutomationElement? ById(AutomationElement root, string automationId) =>
        root.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    public static AutomationElement[] AllByType(AutomationElement root, ControlType type) =>
        root.FindAllDescendants(cf => cf.ByControlType(type));

    /// <summary>アクセスキーの _ を除いた名前</summary>
    public static string Norm(string s) => s.Replace("_", "");

    public static string GetValue(AutomationElement e) => e.Patterns.Value.Pattern.Value.Value;

    public static void SetValue(AutomationElement e, string value) => e.Patterns.Value.Pattern.SetValue(value);

    /// <summary>
    /// Invoke する。モーダルのダイアログを開く操作は、閉じるまで戻ってこないことがあるので、
    /// 別のスレッドで呼んで少しだけ待つ
    /// </summary>
    public static void Invoke(AutomationElement e)
    {
        var pattern = e.Patterns.Invoke.Pattern;
        var task = Task.Run(() =>
        {
            try { pattern.Invoke(); }
            catch (Exception ex) when (Waits.IsTransient(ex)) { }
        });
        task.Wait(TimeSpan.FromMilliseconds(1500));
        Thread.Sleep(150);
    }

    /// <summary>入力欄に文字を入れ、Enter で確定する（Enter か、フォーカスが外れたときに当てはめる入力欄のため）</summary>
    public void EnterValue(AutomationElement edit, string text, AutomationElement? window = null)
    {
        SetValue(edit, text);
        edit.Focus();
        Thread.Sleep(100);
        PressTo(window ?? Main, VirtualKeyShort.ENTER);
        Thread.Sleep(150);
    }

    /// <summary>チェックボックスがオンか</summary>
    public static bool IsOn(AutomationElement checkBox) => checkBox.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On;

    public static void Toggle(AutomationElement checkBox)
    {
        checkBox.Patterns.Toggle.Pattern.Toggle();
        Thread.Sleep(200);
    }

    /// <summary>ラジオボタンを選ぶ（SelectionItem。マウスを使わない）</summary>
    public static void Select(AutomationElement radio)
    {
        radio.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(200);
    }

    /// <summary>ラジオボタンが選ばれているか</summary>
    public static bool IsSelected(AutomationElement radio) => radio.Patterns.SelectionItem.Pattern.IsSelected.Value;

    /// <summary>
    /// メインウィンドウの［×］と同じく、閉じるよう頼む（WindowPattern.Close）。
    /// 確認のメッセージボックスが出ると戻ってこないことがあるので、別のスレッドで呼んで少しだけ待つ
    /// </summary>
    public void RequestClose()
    {
        var pattern = Main.Patterns.Window.Pattern;
        var task = Task.Run(() =>
        {
            try { pattern.Close(); }
            catch (Exception ex) when (Waits.IsTransient(ex)) { }
        });
        task.Wait(TimeSpan.FromMilliseconds(1500));
        Thread.Sleep(150);
    }

    /// <summary>待たずに Invoke する（モーダルを開かないボタン用）</summary>
    public static void InvokeNow(AutomationElement e) => e.Patterns.Invoke.Pattern.Invoke();

    /// <summary>名前が namePrefix で始まるボタンを押す</summary>
    public static void ClickButton(AutomationElement root, string namePrefix)
    {
        var button = Waits.For(() => AllByType(root, ControlType.Button).FirstOrDefault(b => Norm(b.Name).StartsWith(namePrefix)),
            ShortWait, "ボタン " + namePrefix);
        Invoke(button);
        Thread.Sleep(200);
    }

    // ------------------------------------------------------------ メニュー

    private AutomationElement MenuBar() =>
        ById(Main, "MainMenu") ?? Main.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar))
        ?? throw new InvalidOperationException("メニューバーが無い");

    /// <summary>メニューを辿って、最後の項目を実行する。名前は前方一致（アクセスキーの _ は無視）。無効なら例外</summary>
    public void Menu(params string[] path)
    {
        AutomationElement scope = MenuBar();
        for (int i = 0; i < path.Length; i++)
        {
            string want = Norm(path[i]);
            var current = scope;
            var item = Waits.For(() => AllByType(current, ControlType.MenuItem).FirstOrDefault(m => Norm(m.Name).StartsWith(want)),
                ShortWait, "メニュー " + path[i]);
            if (i < path.Length - 1)
            {
                item.Patterns.ExpandCollapse.Pattern.Expand();
                Thread.Sleep(250);
                scope = item;
            }
            else
            {
                if (!item.IsEnabled)
                {
                    CloseMenus();
                    throw new InvalidOperationException($"メニュー {path[i]} が無効");
                }
                Invoke(item);
            }
        }
    }

    /// <summary>メニューを開いて、項目の名前と有効かを返す（閉じてから返す）</summary>
    public IReadOnlyList<(string Name, bool Enabled)> MenuItems(string top)
    {
        var item = Waits.For(() => MenuBar().FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)).FirstOrDefault(m => Norm(m.Name).StartsWith(Norm(top))),
            ShortWait, "メニュー " + top);
        var pattern = item.Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        Thread.Sleep(400);
        var list = item.FindAllChildren(cf => cf.ByControlType(ControlType.MenuItem)).Select(m => (m.Name, m.IsEnabled)).ToList();
        pattern.Collapse();
        Thread.Sleep(200);
        return list;
    }

    public void CloseMenus() => PressEach(Main, VirtualKeyShort.ESCAPE, VirtualKeyShort.ESCAPE);

    // ------------------------------------------------------------ コンボボックス

    private static AutomationElement[] ComboListItems(AutomationElement combo) => AllByType(combo, ControlType.ListItem);

    /// <summary>項目の表示の文字（項目の名前が型名になるものがあるので、中の文字を読む）</summary>
    public static string ItemText(AutomationElement item) =>
        item.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text))?.Name ?? item.Name;

    /// <summary>
    /// コンボボックスの項目を、マウスで選ぶ。UIA の SelectionItemPattern.Select で選ぶと、
    /// そのあと Ctrl つきのキーが効かなくなるため（設計書 6.5）。window はコンボボックスのあるウィンドウ（省けばメイン）
    /// </summary>
    public void SelectCombo(AutomationElement combo, string text, AutomationElement? window = null)
    {
        Activate(window ?? Main);
        Pointer.Click(Pointer.Center(combo.BoundingRectangle));
        var item = Waits.For(() => ComboListItems(combo).FirstOrDefault(i => i.Name == text || ItemText(i) == text),
            ShortWait, "コンボボックスの項目 " + text);
        // 一覧が長いと、項目が見えていないことがある
        if (item.Patterns.ScrollItem.TryGetPattern(out var scroll))
        {
            scroll.ScrollIntoView();
            Thread.Sleep(150);
        }
        Pointer.Click(Pointer.Center(item.BoundingRectangle));
        Waits.Until(() => combo.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed, ShortWait);
        Thread.Sleep(200);
    }

    /// <summary>コンボボックスの項目の文字の一覧（一覧を開いて読み、閉じる）</summary>
    public static IReadOnlyList<string> ComboItems(AutomationElement combo)
    {
        var pattern = combo.Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        Thread.Sleep(300);
        var list = ComboListItems(combo).Select(ItemText).ToList();
        pattern.Collapse();
        Thread.Sleep(200);
        return list;
    }

    /// <summary>コンボボックスで選ばれている項目の文字（一覧を開き、選ばれている項目の中の文字を読む）</summary>
    public static string ComboText(AutomationElement combo)
    {
        var pattern = combo.Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        Thread.Sleep(300);
        var item = ComboListItems(combo).FirstOrDefault(i => i.Patterns.SelectionItem.Pattern.IsSelected.Value);
        string text = item is null ? "" : ItemText(item);
        pattern.Collapse();
        Thread.Sleep(200);
        return text;
    }

    // ------------------------------------------------------------ メッセージボックス・ダイアログ

    /// <summary>メッセージボックス（#32770 で、タイトルがアプリ名）</summary>
    public AutomationElement? FindMessageBox() =>
        AllWindows().FirstOrDefault(w => w.ClassName == "#32770" && w.Name == AppName);

    /// <summary>メッセージボックスを待ち、本文と一緒に返す</summary>
    public (AutomationElement Box, string Text) WaitMessageBox(TimeSpan? timeout = null)
    {
        var box = Waits.For(FindMessageBox, timeout ?? TimeSpan.FromSeconds(10), "メッセージボックス");
        Thread.Sleep(200);
        string text = string.Join("\n", AllByType(box, ControlType.Text).Select(t => t.Name));
        return (box, text);
    }

    /// <summary>メッセージボックスのボタンを押して閉じ、本文を返す</summary>
    public string AnswerMessageBox(string button, TimeSpan? timeout = null)
    {
        var (box, text) = WaitMessageBox(timeout);
        ClickButton(box, button);
        Waits.Until(() => FindMessageBox() is null, ShortWait);
        return text;
    }

    /// <summary>WPF のダイアログ（タイトルが titlePrefix で始まる、メイン以外のウィンドウ）を待つ</summary>
    public AutomationElement WaitDialog(string titlePrefix, TimeSpan? timeout = null) =>
        Waits.For(() => AllWindows().FirstOrDefault(w => w.ClassName != "#32770" && w.Name.StartsWith(titlePrefix) && !w.Equals(Main)),
            timeout ?? TimeSpan.FromSeconds(10), "ダイアログ " + titlePrefix);

    /// <summary>コモン ダイアログ（開く・フォルダーの選択。#32770 で、タイトルがアプリ名でないもの）</summary>
    public AutomationElement? FindFileDialog() =>
        AllWindows().FirstOrDefault(w => w.ClassName == "#32770" && w.Name != AppName);

    /// <summary>
    /// コモン ダイアログのファイル名の入力欄。「開く」は id 1148、フォルダーの選択は名前が「フォルダー:」。
    /// OS が日本語のときの名前で探す（設計書 6.5。CI で動かさない理由の 1 つ）
    /// </summary>
    public static AutomationElement FileNameEdit(AutomationElement dialog)
    {
        var found = Waits.For(() => ById(dialog, "1148")
                ?? AllByType(dialog, ControlType.Edit).FirstOrDefault(e => e.Name.StartsWith("フォルダー") || e.Name.StartsWith("ファイル名")),
            ShortWait, "ファイル名の入力欄");
        if (found.ControlType == ControlType.Edit) return found;
        return found.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)) ?? found;
    }

    /// <summary>コモン ダイアログで今いるフォルダ（アドレス バーの「アドレス: C:\…」の名前から読む）</summary>
    public static string FileDialogFolder(AutomationElement dialog)
    {
        var address = AllByType(dialog, ControlType.ToolBar).Select(t => t.Name).FirstOrDefault(n => n.StartsWith("アドレス")) ?? "";
        int colon = address.IndexOf(':');
        return colon < 0 ? "" : address[(colon + 1)..].Trim();
    }

    /// <summary>コモン ダイアログのファイル名の入力欄に入れて Enter（［開く］は分割ボタンで押せないため）</summary>
    public void FileDialogEnter(AutomationElement dialog, string text)
    {
        var edit = FileNameEdit(dialog);
        SetValue(edit, text);
        edit.Focus();
        Thread.Sleep(150);
        PressTo(dialog, VirtualKeyShort.ENTER);
    }

    /// <summary>「開く」のダイアログ（Ctrl+O）で、フォルダの中のファイルを開く（複数なら "a" "b" の形で入れる）</summary>
    public void OpenViaDialog(string folder, params string[] files)
    {
        Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_O);
        var dialog = Waits.For(FindFileDialog, TimeSpan.FromSeconds(10), "開くダイアログ");
        Thread.Sleep(600);
        FileDialogEnter(dialog, folder);
        Thread.Sleep(900);
        dialog = Waits.For(FindFileDialog, ShortWait, "開くダイアログ");
        FileDialogEnter(dialog, files.Length == 1 ? files[0] : string.Join(" ", files.Select(f => $"\"{f}\"")));
        Waits.Until(() => FindFileDialog() is null, ShortWait);
    }

    // ------------------------------------------------------------ キー

    /// <summary>
    /// ウィンドウを手前にする。Alt キーを使う方法は、WPF のメニューにフォーカスが移ってしまうので使わない。
    /// 手前にならなければ、タイトルバー（タイトルの文字の右の空き）をクリックする（マウスの歯止めを通る）
    /// </summary>
    public static void Activate(AutomationElement window)
    {
        var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd != IntPtr.Zero && BringToFront(hwnd)) return;

        var title = window.FindFirstChild(cf => cf.ByControlType(ControlType.TitleBar));
        var r = (title ?? window).BoundingRectangle;
        Pointer.Click(new Point(r.Left + (int)Math.Min(r.Width * 0.6, r.Width - 160), r.Top + Math.Min(12, r.Height / 2)));
        Thread.Sleep(250);
    }

    /// <summary>
    /// ウィンドウを手前にする（手前になれば true）。手前のウィンドウのスレッドに入力をつないでから SetForegroundWindow を呼ぶ
    /// （ほかのプロセスが手前にあると、そのままでは手前にできないため）。Alt キーは使わない
    /// </summary>
    public static bool BringToFront(IntPtr hwnd)
    {
        if (Native.GetForegroundWindow() == hwnd) return true;
        uint foreground = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        uint me = Native.GetCurrentThreadId();
        Native.AttachThreadInput(me, foreground, true);
        Native.SetForegroundWindow(hwnd);
        Native.AttachThreadInput(me, foreground, false);
        Thread.Sleep(150);
        return Native.GetForegroundWindow() == hwnd;
    }

    /// <summary>手前のウィンドウがこのアプリのものか</summary>
    public bool IsForeground()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        return pid == ProcessId;
    }

    /// <summary>
    /// キーを送る前に、対象のウィンドウを手前にし、このアプリが最前面かを確かめる。
    /// 違えば、ほかのアプリに入力しないよう、送らずに止める（設計書 6.5）
    /// </summary>
    private void EnsureForeground(AutomationElement target)
    {
        Activate(target);
        if (IsForeground()) return;
        Activate(target);
        if (!IsForeground()) throw new InvalidOperationException($"{AppName} が手前に無いので、キーを送らずに止めた");
    }

    /// <summary>メインウィンドウに、キーを同時に押して送る（例: Press(CONTROL, KEY_O) で Ctrl+O）</summary>
    public void Press(params VirtualKeyShort[] keys) => PressTo(Main, keys);

    /// <summary>そのウィンドウに、キーを同時に押して送る</summary>
    public void PressTo(AutomationElement target, params VirtualKeyShort[] keys)
    {
        EnsureForeground(target);
        Keyboard.TypeSimultaneously(keys);
        Thread.Sleep(250);
    }

    /// <summary>そのウィンドウに、キーを 1 つずつ順に送る（例: Esc を 2 回）</summary>
    public void PressEach(AutomationElement target, params VirtualKeyShort[] keys)
    {
        EnsureForeground(target);
        Keyboard.Type(keys);
        Thread.Sleep(250);
    }

    /// <summary>文字を打つ（フォーカスのある入力欄へ）</summary>
    public void TypeText(string text, AutomationElement? target = null)
    {
        EnsureForeground(target ?? Main);
        Keyboard.Type(text);
        Thread.Sleep(250);
    }
}
