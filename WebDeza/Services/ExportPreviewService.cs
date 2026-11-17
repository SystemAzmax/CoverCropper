using System.IO;
using WebDeza.Imaging;
using WebDeza.Imaging.Editing;
using WebDeza.Imaging.Formats;

namespace WebDeza.Services;

/// <summary>
/// 保存サイズの計算の結果（設計書 5.6）
/// </summary>
/// <param name="Encoded">エンコードした中身（バイト数・使ったオプション・上限に収まったか）</param>
/// <param name="Width">保存する画像の幅</param>
/// <param name="Height">保存する画像の高さ</param>
/// <param name="Rendered">圧縮前（保存する画像。透過を保てない形式なら背景色で塗ったもの）</param>
/// <param name="Decoded">圧縮後（エンコードした中身を、同じ形式のデコーダーで読み直したもの）。読み直せない形式は null（M27）</param>
/// <param name="Source">計算の元にした今の画像（保存するときに、同じ条件の結果かを見分けるため）</param>
/// <param name="Settings">計算に使った保存の設定</param>
public sealed record ExportPreviewResult(
    EncodedImage Encoded, int Width, int Height,
    ImageBuffer Rendered,
    ImageBuffer? Decoded,
    ImageBuffer Source,
    ExportSettings Settings)
{
    /// <summary>保存したときのファイルのバイト数（保存するときはこのバイト列をそのまま書くので一致する）</summary>
    public long FileSize => Encoded.Bytes.LongLength;

    /// <summary>この結果が、image を settings で保存するときと同じ条件で作ったものか</summary>
    public bool Matches(ImageBuffer image, ExportSettings settings)
        => ReferenceEquals(Source, image) && Settings.Equals(settings);
}

/// <summary>
/// 保存のダイアログの、保存サイズの表示とプレビュー用の計算（設計書 F-09・5.6・5.8）。
/// 実際に保存するのと同じ処理（ExportRenderer.Render → ExportEncoder.Encode）でメモリ上にファイルの中身を作り、
/// 同じ形式のデコーダーで読み直して圧縮後の画像を作る。
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>最後に作った「保存する画像」を、条件（元の画像・背景色・形式が透過を保てるか）と一緒に覚えておき、
/// 条件が同じなら作り直さない（品質のスライダーなど、オプションや上限だけが変わったときは、エンコードだけをやり直す）</item>
/// <item>新しい依頼が来たら、前の計算を取り消す。古い計算の結果は捨て、最後の依頼の結果だけを Completed で知らせる</item>
/// <item>計算はバックグラウンドで行う。RequestAsync は UI のスレッドから呼び、Completed・Failed は呼んだスレッド
/// （の同期コンテキスト）で出る</item>
/// </list>
/// </remarks>
public sealed class ExportPreviewService
{
    private readonly FormatRegistry _formats;
    private readonly ExportEncoder _encoder;
    private readonly Lock _cacheLock = new();

    /// <summary>覚えている保存する画像と、その条件</summary>
    private (ImageBuffer Source, RgbColor Background, bool KeepsAlpha, ImageBuffer Rendered)? _rendered;

    private CancellationTokenSource? _cancellation;
    private long _version;
    private int _renderCount;
    private int _encodeCount;

    public ExportPreviewService(FormatRegistry formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
        _encoder = new ExportEncoder(formats);
    }

    /// <summary>最後の依頼の結果（まだ無い・失敗した・計算中なら、前の依頼の結果のまま。失敗したら null）</summary>
    public ExportPreviewResult? Latest { get; private set; }

    /// <summary>最後の依頼の計算が終わった（古い依頼の結果では出ない）</summary>
    public event EventHandler<ExportPreviewResult>? Completed;

    /// <summary>最後の依頼の計算に失敗した（形式プラグインの例外など。表示を「—」にしてログに書くのは受ける側）</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>保存する画像を作った回数（ExportRenderer.Render を呼んだ回数。確かめるため）</summary>
    public int RenderCount => Volatile.Read(ref _renderCount);

    /// <summary>エンコードした回数（確かめるため）</summary>
    public int EncodeCount => Volatile.Read(ref _encodeCount);

    /// <summary>
    /// 今の画像を保存の設定で保存したときのサイズと、圧縮前・圧縮後の画像を計算する（バックグラウンド）。
    /// 前の依頼の計算は取り消す。この依頼が最後の依頼のまま終われば、結果を Latest に入れて Completed を出し、結果を返す。
    /// 途中で次の依頼が来たら、結果は捨てて null を返す。失敗したら Failed を出して null を返す
    /// </summary>
    /// <param name="image">今の画像（編集を確定した結果の全体）</param>
    /// <param name="settings">保存の設定</param>
    public async Task<ExportPreviewResult?> RequestAsync(ImageBuffer image, ExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        long version = ++_version;
        var token = cancellation.Token;

        // 同じ条件の結果が既にあれば、計算し直さない
        if (Latest is { } latest && latest.Matches(image, settings))
        {
            Completed?.Invoke(this, latest);
            return latest;
        }

        ExportPreviewResult? result = null;
        Exception? error = null;
        try
        {
            result = await Task.Run(() => Compute(image, settings, token), token);
        }
        catch (OperationCanceledException)
        {
            // 次の依頼が来た
        }
        catch (Exception ex)
        {
            error = ex;
        }

        if (version != _version) return null; // 古い依頼の結果は捨てる

        if (error is not null)
        {
            Latest = null;
            Failed?.Invoke(this, error);
            return null;
        }
        if (result is null) return null;

        Latest = result;
        Completed?.Invoke(this, result);
        return result;
    }

    /// <summary>計算中の依頼を取り消す（ダイアログを閉じるとき）</summary>
    public void Cancel()
    {
        _version++;
        _cancellation?.Cancel();
    }

    private ExportPreviewResult Compute(ImageBuffer image, ExportSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var format = _formats.FindById(settings.FormatId);
        if (format is null || !format.Capabilities.HasFlag(FormatCapabilities.Encode) || format.Encoder is null)
            throw new UnsupportedImageFormatException($"保存できない形式です: {settings.FormatId}", settings.FormatId, null);

        bool keepsAlpha = format.Capabilities.HasFlag(FormatCapabilities.Alpha);
        var rendered = GetRendered(image, settings, keepsAlpha, token);

        Interlocked.Increment(ref _encodeCount);
        var encoded = _encoder.Encode(rendered, settings, token);
        token.ThrowIfCancellationRequested();

        var decoded = TryDecode(format, encoded.Bytes, token);
        return new ExportPreviewResult(encoded, rendered.Width, rendered.Height, rendered, decoded, image, settings);
    }

    /// <summary>保存する画像: 条件（元の画像・背景色・透過を保てるか）が前回と同じなら使い回す</summary>
    private ImageBuffer GetRendered(ImageBuffer image, ExportSettings settings, bool keepsAlpha, CancellationToken token)
    {
        lock (_cacheLock)
        {
            if (_rendered is { } cached && ReferenceEquals(cached.Source, image)
                && cached.Background == settings.Background && cached.KeepsAlpha == keepsAlpha)
            {
                return cached.Rendered;
            }
        }

        Interlocked.Increment(ref _renderCount);
        var rendered = ExportRenderer.Render(image, settings, keepsAlpha, token);
        lock (_cacheLock) _rendered = (image, settings.Background, keepsAlpha, rendered);
        return rendered;
    }

    /// <summary>エンコードした中身を、同じ形式のデコーダーで読み直す。デコーダーが無い・読めないときは null（M27）</summary>
    private static ImageBuffer? TryDecode(IImageFormat format, byte[] bytes, CancellationToken token)
    {
        if (!format.Capabilities.HasFlag(FormatCapabilities.Decode) || format.Decoder is null) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return format.Decoder.Decode(stream, token).Image;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 読み直せなくても、サイズは出せる
            return null;
        }
    }
}
