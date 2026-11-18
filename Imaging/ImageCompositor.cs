using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;

namespace AreaShot.Imaging;

/// <summary>
/// 出力時の合成（仕様書 §9.1・§12）。元画像と編集オブジェクトを元画像と同じピクセルサイズで 1 枚にする。
/// 表示倍率は関係しない。
/// </summary>
public static class ImageCompositor
{
    /// <summary>
    /// 合成した凍結済み画像を返す。編集オブジェクトが無ければ元画像をそのまま返す（再描画しない＝画素が完全に一致する）。
    /// トリミング（V1.4.0）があれば、合成した後にその範囲（整数ピクセル・画像内）へ切り出す。
    /// </summary>
    public static BitmapSource Compose(BitmapSource baseImage, IReadOnlyCollection<Annotation> annotations, MosaicCache mosaics)
    {
        int w = baseImage.PixelWidth, h = baseImage.PixelHeight;
        var drawn = annotations.Where(a => a.Kind != AnnotationKind.Crop).ToList();
        var composed = drawn.Count == 0 ? baseImage : Render(baseImage, drawn, mosaics);
        if (CropMath.EffectiveRect(annotations, w, h) is not { } crop || (crop.Width == w && crop.Height == h)) return composed;

        var cropped = new CroppedBitmap(composed, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        cropped.Freeze();
        return cropped;
    }

    private static BitmapSource Render(BitmapSource baseImage, IReadOnlyCollection<Annotation> annotations, MosaicCache mosaics)
    {
        int w = baseImage.PixelWidth, h = baseImage.PixelHeight;
        var visual = new DrawingVisual();
        // 元画像は等倍で描くので補間は起きないが、念のため最近傍にしてぼけを防ぐ。
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Ideal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, w, h));
            AnnotationRenderer.DrawAll(dc, annotations, mosaics, w, h);
        }

        var target = new RenderTargetBitmap(w, h, BitmapConvert.Dpi, BitmapConvert.Dpi, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
