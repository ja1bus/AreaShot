namespace AreaShot.Core.Geometry;

/// <summary>
/// 画像座標 ⇔ 表示座標（WPF の論理単位 DIU）の変換（仕様書 §9.4・§13）。
/// 編集オブジェクトは常に画像座標で保持し、表示時だけこの変換を掛ける。
/// </summary>
public readonly record struct ViewTransform(double Scale)
{
    /// <summary>
    /// 表示倍率とモニターの拡大率から、1 画像ピクセルあたりの DIU を求める。
    /// 表示倍率 100% は「1 画像ピクセル = 1 物理ピクセル」とするため、DPI 倍率で割る。
    /// </summary>
    public static ViewTransform For(double zoom, double dpiScale) =>
        new(zoom / (dpiScale > 0 ? dpiScale : 1.0));

    public PointD ToView(PointD image) => new(image.X * Scale, image.Y * Scale);

    public PointD ToImage(PointD view) => new(view.X / Scale, view.Y / Scale);

    public RectD ToView(RectD image) => new(image.X * Scale, image.Y * Scale, image.Width * Scale, image.Height * Scale);

    /// <summary>表示上の長さ（DIU）を画像上の長さへ変換する（当たり判定の許容幅など）。</summary>
    public double ToImageLength(double viewLength) => viewLength / Scale;
}

/// <summary>表示倍率のプリセットと Ctrl+ホイールの段階移動（仕様書 §9.4）。</summary>
public static class ZoomMath
{
    public static readonly IReadOnlyList<double> Presets = [0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0];

    public const double Min = 0.25;
    public const double Max = 2.0;
    private const double Epsilon = 1e-6;

    /// <summary>今の倍率より 1 段大きいプリセット（上限で止まる）。</summary>
    public static double StepIn(double zoom) =>
        Presets.FirstOrDefault(p => p > zoom + Epsilon, Max);

    /// <summary>今の倍率より 1 段小さいプリセット（下限で止まる）。</summary>
    public static double StepOut(double zoom) =>
        Presets.LastOrDefault(p => p < zoom - Epsilon, Min);

    /// <summary>
    /// 「ウィンドウに合わせる」の倍率。表示領域（物理ピクセル）に画像全体が収まる最大の倍率で、100% を超えて拡大はしない。
    /// </summary>
    public static double Fit(int imageWidth, int imageHeight, double availableWidthPx, double availableHeightPx)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || availableWidthPx <= 0 || availableHeightPx <= 0) return 1.0;
        var z = Math.Min(availableWidthPx / imageWidth, availableHeightPx / imageHeight);
        return Math.Clamp(z, 0.05, 1.0);
    }

    public static string Format(double zoom) => $"{Math.Round(zoom * 100)}%";
}
