namespace WebDeza.Imaging.Geometry;

/// <summary>
/// 回転・反転の組み合わせ。「左右反転 → 時計回りに Rotation 度の回転」の順に適用する。
/// 8 通りあり、EXIF Orientation の 1〜8 と 1 対 1 に対応する。default は「そのまま」。
/// </summary>
public readonly record struct ImageOrientation
{
    /// <param name="rotation">時計回りの回転角。0・90・180・270 のいずれか</param>
    /// <param name="flipHorizontal">回転の前に左右反転するか</param>
    public ImageOrientation(int rotation, bool flipHorizontal)
    {
        if (rotation is not (0 or 90 or 180 or 270))
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "回転角は 0・90・180・270 のいずれかにしてください。");

        Rotation = rotation;
        FlipHorizontal = flipHorizontal;
    }

    /// <summary>そのまま（回転も反転もしない）</summary>
    public static ImageOrientation Identity => default;

    /// <summary>時計回りの回転角（0・90・180・270）</summary>
    public int Rotation { get; }

    /// <summary>回転の前に左右反転するか</summary>
    public bool FlipHorizontal { get; }

    public bool IsIdentity => Rotation == 0 && !FlipHorizontal;

    /// <summary>適用すると幅と高さが入れ替わるか（90°・270° の回転を含むとき）</summary>
    public bool SwapsDimensions => Rotation is 90 or 270;

    // ---------------------------------------------------------------- 操作
    // どれも「今の向きを適用した画像」に対する、見た目どおりの操作を足した向きを返す。
    // 反転は回転の後に行うことになるので、回転の向きが逆になる（F∘R(r) = R(−r)∘F）。

    /// <summary>右（時計回り）に 90° 回転する</summary>
    public ImageOrientation RotateRight() => new((Rotation + 90) % 360, FlipHorizontal);

    /// <summary>左（反時計回り）に 90° 回転する</summary>
    public ImageOrientation RotateLeft() => new((Rotation + 270) % 360, FlipHorizontal);

    /// <summary>左右反転する</summary>
    public ImageOrientation FlipHorizontally() => new((360 - Rotation) % 360, !FlipHorizontal);

    /// <summary>上下反転する</summary>
    public ImageOrientation FlipVertically() => new((540 - Rotation) % 360, !FlipHorizontal);

    // ---------------------------------------------------------------- EXIF

    /// <summary>対応する EXIF Orientation（1〜8）</summary>
    public int ToExif() => (Rotation, FlipHorizontal) switch
    {
        (0, false) => 1,   // そのまま
        (90, false) => 6,  // 時計回り 90°
        (180, false) => 3, // 180°
        (270, false) => 8, // 反時計回り 90°
        (0, true) => 2,    // 左右反転
        (90, true) => 7,   // 左右反転 → 時計回り 90°（反転置）
        (180, true) => 4,  // 上下反転
        _ => 5,            // (270, true): 左右反転 → 反時計回り 90°（転置）
    };

    /// <summary>EXIF Orientation に対応する向き。1〜8 以外の値は「そのまま」として扱う</summary>
    public static ImageOrientation FromExif(int exifOrientation) => exifOrientation switch
    {
        2 => new(0, true),
        3 => new(180, false),
        4 => new(180, true),
        5 => new(270, true),
        6 => new(90, false),
        7 => new(90, true),
        8 => new(270, false),
        _ => Identity,
    };
}
