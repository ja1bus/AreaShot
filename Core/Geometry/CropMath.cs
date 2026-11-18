using AreaShot.Core.Annotations;

namespace AreaShot.Core.Geometry;

/// <summary>トリミングの範囲の計算（V1.4.0。仕様書 §10・§24.2）。</summary>
public static class CropMath
{
    /// <summary>
    /// トリミングの範囲を整数ピクセルに丸めて画像内に収める（各辺を <see cref="Math.Round(double)"/>。モザイクと同じ偶数丸め）。
    /// 空なら「トリミングなし」として扱う。
    /// </summary>
    public static PixelRect ToPixelRect(RectD bounds, int imageWidth, int imageHeight)
    {
        var l = (int)Math.Round(bounds.Left);
        var t = (int)Math.Round(bounds.Top);
        var r = (int)Math.Round(bounds.Right);
        var b = (int)Math.Round(bounds.Bottom);
        return PixelRect.FromEdges(l, t, r, b).Intersect(new PixelRect(0, 0, imageWidth, imageHeight));
    }

    /// <summary>出力で切り出す範囲。トリミングが無い・空なら null（画像全体を出力する）。</summary>
    public static PixelRect? EffectiveRect(IEnumerable<Annotation> annotations, int imageWidth, int imageHeight)
    {
        if (AnnotationOps.CropOf(annotations) is not { } crop) return null;
        var rect = ToPixelRect(crop.Bounds, imageWidth, imageHeight);
        return rect.IsEmpty ? null : rect;
    }

    /// <summary>出力画像の大きさ（トリミングが無い・空なら元画像の大きさ）。</summary>
    public static (int Width, int Height) OutputSize(int imageWidth, int imageHeight, IEnumerable<Annotation> annotations) =>
        EffectiveRect(annotations, imageWidth, imageHeight) is { } r ? (r.Width, r.Height) : (imageWidth, imageHeight);
}
