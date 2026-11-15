using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WebDeza.Views;
using WebDeza.Views.Canvas;

namespace WebDeza.Tests.Views;

/// <summary>道具の仕組み・手のひら・キー入力の決まり（設計書 F-03・4.4・5.6。V41。実装計画書 8-4）</summary>
public class ToolTests
{
    // ---------------------------------------------------------------- V41: ToolRegistry

    [Fact]
    public void ToolRegistry_KeepsRegistrationOrder()
    {
        var tools = new ToolRegistry();
        var select = new FakeToolFactory("select", "選択", Key.M);
        var hand = new HandToolFactory();

        tools.Register(select);
        tools.Register(hand);

        Assert.Equal(new ICanvasToolFactory[] { select, hand }, tools.All);
        Assert.Same(hand, tools.Find("hand"));
        Assert.Null(tools.Find("HAND")); // 大文字小文字を区別する
    }

    [Fact]
    public void ToolRegistry_SameId_Throws()
    {
        var tools = new ToolRegistry();
        tools.Register(new HandToolFactory());

        Assert.Throws<ArgumentException>(() => tools.Register(new FakeToolFactory("hand", "別の手", Key.J)));
        Assert.Single(tools.All);
    }

    [Fact]
    public void ToolRegistry_EmptyIdOrNull_Throws()
    {
        var tools = new ToolRegistry();

        Assert.Throws<ArgumentException>(() => tools.Register(new FakeToolFactory("", "名無し", Key.N)));
        Assert.Throws<ArgumentNullException>(() => tools.Register(null!));
    }

    // ---------------------------------------------------------------- 手のひら

    [Fact]
    public void HandToolFactory_IsHandWithH()
    {
        var factory = new HandToolFactory();

        Assert.Equal(("hand", "手のひら", Key.H), (factory.Id, factory.DisplayName, factory.Shortcut));
        Assert.IsType<HandTool>(factory.Create(new FakeSession(), new FakeHost()));
    }

    [Fact]
    public void HandTool_LeftDown_BeginsPanOnHost()
    {
        var host = new FakeHost { CanPan = true };
        var tool = new HandToolFactory().Create(new FakeSession(), host);
        var pointer = new CanvasPointer(new Point(10, 20), new Point(100, 200), MouseButton.Left, ModifierKeys.None);

        tool.OnPointerDown(pointer);
        tool.OnPointerMove(pointer with { ViewPoint = new Point(30, 40) });
        tool.OnPointerUp(pointer);

        // パンそのもの（動かす・ボタンを離して終える）はキャンバスが行う
        Assert.Equal(new[] { pointer }, host.PanRequests);
    }

    [Theory]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.Middle)]
    public void HandTool_OtherButtons_DoNothing(MouseButton button)
    {
        var host = new FakeHost();
        var tool = new HandToolFactory().Create(new FakeSession(), host);

        tool.OnPointerDown(new CanvasPointer(default, default, button, ModifierKeys.None));

        Assert.Empty(host.PanRequests);
    }

    [Fact]
    public void HandTool_CursorOnlyWhenCanPan_AndUsesNoKeys()
    {
        var host = new FakeHost();
        var tool = new HandToolFactory().Create(new FakeSession(), host);
        var pointer = new CanvasPointer(default, default, null, ModifierKeys.None);

        Assert.Null(tool.GetCursor(pointer)); // 全体表示では、パンしても何もしない
        host.CanPan = true;
        Assert.Same(Cursors.SizeAll, tool.GetCursor(pointer));
        Assert.False(tool.OnKeyDown(Key.Left, ModifierKeys.None));
    }

    // ---------------------------------------------------------------- キー入力の決まり（設計書 4.4・5.10-7）

    [Theory]
    [InlineData(Key.H, ModifierKeys.None, true)]
    [InlineData(Key.Left, ModifierKeys.None, true)]
    [InlineData(Key.Left, ModifierKeys.Shift, true)]       // Shift+矢印は道具へ
    [InlineData(Key.H, ModifierKeys.Control, false)]       // Ctrl つきはメニューのキー
    [InlineData(Key.H, ModifierKeys.Alt, false)]
    [InlineData(Key.System, ModifierKeys.None, false)]     // Alt でメニューへ
    [InlineData(Key.LeftShift, ModifierKeys.Shift, false)] // 修飾キーそのもの
    [InlineData(Key.Tab, ModifierKeys.None, false)]
    [InlineData(Key.ImeProcessed, ModifierKeys.None, false)]
    public void ShouldRouteToTools_ByKeyAndModifiers(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, KeyRouting.ShouldRouteToTools(key, modifiers, inputControlFocused: false));
    }

    [Fact]
    public void ShouldRouteToTools_InputControlFocused_IsFalse()
    {
        Assert.False(KeyRouting.ShouldRouteToTools(Key.H, ModifierKeys.None, inputControlFocused: true));
    }

    [Fact]
    public void IsToolShortcut_OnlyWithoutModifiers()
    {
        Assert.True(KeyRouting.IsToolShortcut(ModifierKeys.None));
        Assert.False(KeyRouting.IsToolShortcut(ModifierKeys.Shift));
    }

    [Fact]
    public void IsInputControl_TextBoxSliderComboAndMenu()
    {
        RunSta(() =>
        {
            Assert.True(KeyRouting.IsInputControl(new TextBox()));
            Assert.True(KeyRouting.IsInputControl(new Slider()));
            Assert.True(KeyRouting.IsInputControl(new ComboBox()));
            var menu = new Menu();
            var item = new MenuItem();
            menu.Items.Add(item);
            Assert.True(KeyRouting.IsInputControl(item));

            // 入力欄の中の要素（論理ツリーの子）も入力欄として扱う
            var panel = new StackPanel();
            var box = new TextBox();
            panel.Children.Add(box);
            Assert.True(KeyRouting.IsInputControl(box));

            Assert.False(KeyRouting.IsInputControl(new Button()));
            Assert.False(KeyRouting.IsInputControl(new EditorCanvas()));
            Assert.False(KeyRouting.IsInputControl(null));
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}

/// <summary>テスト用の道具の種類（何もしない道具を作る）</summary>
internal sealed class FakeToolFactory(string id, string displayName, Key shortcut) : ICanvasToolFactory
{
    public string Id => id;
    public string DisplayName => displayName;
    public Key Shortcut => shortcut;

    public ICanvasTool Create(IEditorSession session, ICanvasHost host) => new HandTool(host);
}

/// <summary>テスト用のキャンバス（道具から頼まれたことを覚える）</summary>
internal sealed class FakeHost : ICanvasHost
{
    public List<CanvasPointer> PanRequests { get; } = new();
    public ViewTransform View { get; set; } = new(1, 0, 0);
    public bool CanPan { get; set; }
    public int InvalidateCount { get; private set; }
    public bool Captured { get; private set; }

    public void InvalidateVisual() => InvalidateCount++;
    public void BeginPan(CanvasPointer pointer) => PanRequests.Add(pointer);
    public bool CaptureMouse() => Captured = true;
    public void ReleaseMouseCapture() => Captured = false;
}

/// <summary>テスト用の画像の窓口</summary>
internal sealed class FakeSession : IEditorSession
{
    public int ImageWidth { get; set; } = 100;
    public int ImageHeight { get; set; } = 80;
    public WebDeza.Imaging.PixelRect? Selection { get; set; }
    public WebDeza.Imaging.AspectRatio? Aspect { get; set; }
    public WebDeza.Imaging.Editing.FrameRules SelectionRules
        => WebDeza.Imaging.Editing.CropFrameCalculator.Rules(ImageWidth, ImageHeight, Aspect);
#pragma warning disable CS0067 // テストでは使わない
    public event EventHandler? Changed;
#pragma warning restore CS0067
}
