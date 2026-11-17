using System.Windows;
using WebDeza.Views.Canvas;
using WebDeza.Views.Dialogs;

namespace WebDeza.Tests.Views;

/// <summary>保存のダイアログのプレビューの表示（倍率と位置）。圧縮前と圧縮後で共有する（設計書 F-09。実装計画書 10-8・10-9）</summary>
public class PreviewViewportTests
{
    [Fact]
    public void Initially_IsInitialWithoutView()
    {
        var viewport = new PreviewViewport();

        Assert.True(viewport.IsInitial);
        Assert.Null(viewport.Zoomed);
    }

    [Fact]
    public void Set_NotifiesAllSharingCanvases()
    {
        var viewport = new PreviewViewport();
        int changed = 0;
        viewport.Changed += (_, _) => changed++; // 圧縮前のキャンバス
        viewport.Changed += (_, _) => changed++; // 圧縮後のキャンバス

        viewport.Set(new ViewTransform(2, 10, 20), new Size(400, 300));

        Assert.Equal(2, changed);
        Assert.False(viewport.IsInitial);
        Assert.Equal(new ViewTransform(2, 10, 20), viewport.Zoomed);
        Assert.Equal(new Size(400, 300), viewport.Area);
    }

    [Fact]
    public void Resize_SharedBySameSizedCanvases_AppliesOnce()
    {
        // 「並べて比較」の 2 つのキャンバスは同じ大きさで続けて大きさの変化を知らせる。計算は 1 回だけ（中心がずれないように）
        var viewport = new PreviewViewport();
        var view = new ViewTransform(1, -100, -50);
        viewport.Set(view, new Size(400, 300));
        var expected = view.Resized(new Size(400, 300), new Size(600, 300)).ClampPan(new Size(600, 300), 2000, 1000);

        viewport.Resize(new Size(600, 300), 2000, 1000); // 圧縮前
        viewport.Resize(new Size(600, 300), 2000, 1000); // 圧縮後

        Assert.Equal(expected, viewport.Zoomed);
        Assert.Equal(new Size(600, 300), viewport.Area);
    }

    [Fact]
    public void Resize_ToZero_IsIgnored()
    {
        // 「結果のみ」にして隠した圧縮前のキャンバスの大きさは 0 になる
        var viewport = new PreviewViewport();
        var view = new ViewTransform(1, -100, -50);
        viewport.Set(view, new Size(400, 300));

        viewport.Resize(new Size(0, 0), 2000, 1000);

        Assert.Equal(view, viewport.Zoomed);
        Assert.Equal(new Size(400, 300), viewport.Area);
    }

    [Fact]
    public void Resize_WhenFitOrInitial_OnlyRemembersArea()
    {
        var viewport = new PreviewViewport();
        viewport.Resize(new Size(400, 300), 2000, 1000);
        Assert.True(viewport.IsInitial); // 最初の 100% は、描くときに決める
        Assert.Equal(new Size(400, 300), viewport.Area);

        viewport.Set(null, new Size(400, 300)); // 全体表示
        viewport.Resize(new Size(800, 600), 2000, 1000);
        Assert.Null(viewport.Zoomed); // 描くたびに今の大きさで全体表示する
    }

    [Fact]
    public void Reset_ReturnsToInitial()
    {
        var viewport = new PreviewViewport();
        viewport.Set(new ViewTransform(2, 0, 0), new Size(400, 300));
        int changed = 0;
        viewport.Changed += (_, _) => changed++;

        viewport.Reset();

        Assert.True(viewport.IsInitial);
        Assert.Null(viewport.Zoomed);
        Assert.Equal(1, changed);
    }
}
