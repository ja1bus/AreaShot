using AreaShot.Core.Geometry;

namespace AreaShot.Core.Imaging;

/// <summary>
/// 矩形モザイク（仕様書 §10.4）。元画像の指定範囲を固定の大きさのブロックに分け、各ブロックを平均色で塗る。
/// 常に元画像（キャプチャー直後の画素）から生成するため、表示倍率や他の注釈の影響を受けない。
/// </summary>
public static class Mosaic
{
    /// <summary>
    /// ブロックの一辺（元画像のピクセル）。固定。
    /// 一般的な画面の文字（高さ 12～24px 程度）がブロック 1～2 個分に潰れ、読めなくなる大きさにしている。
    /// </summary>
    public const int BlockSize = 16;

    /// <summary>モザイクの範囲（実数の画像座標）を画素範囲へ丸め、画像内へ切り詰める。</summary>
    public static PixelRect ToPixelRect(RectD bounds, int imageWidth, int imageHeight)
    {
        var l = (int)Math.Round(bounds.Left);
        var t = (int)Math.Round(bounds.Top);
        var r = (int)Math.Round(bounds.Right);
        var b = (int)Math.Round(bounds.Bottom);
        return PixelRect.FromEdges(l, t, r, b).Intersect(new PixelRect(0, 0, imageWidth, imageHeight));
    }

    /// <summary>
    /// <paramref name="source"/> の <paramref name="region"/>（画像内に切り詰め済み）をモザイク化した画素を返す。
    /// 返す画像の大きさは region と同じ。ブロックの区切りは region の左上を起点とする（移動してもブロックの並びが崩れない）。
    /// </summary>
    public static PixelBuffer Pixelate(PixelBuffer source, PixelRect region, int blockSize = BlockSize)
    {
        region = region.Intersect(source.Bounds);
        if (region.IsEmpty) throw new ArgumentException("The mosaic region is outside the image.", nameof(region));
        if (blockSize < 1) blockSize = 1;

        var result = PixelBuffer.Create(region.Width, region.Height);
        var src = source.Pixels;
        var dst = result.Pixels;
        var srcStride = source.Stride;
        var dstStride = result.Stride;

        for (var by = 0; by < region.Height; by += blockSize)
        {
            var bh = Math.Min(blockSize, region.Height - by);
            for (var bx = 0; bx < region.Width; bx += blockSize)
            {
                var bw = Math.Min(blockSize, region.Width - bx);
                long sb = 0, sg = 0, sr = 0, sa = 0;
                for (var y = 0; y < bh; y++)
                {
                    var i = (region.Top + by + y) * srcStride + (region.Left + bx) * 4;
                    for (var x = 0; x < bw; x++, i += 4)
                    {
                        sb += src[i];
                        sg += src[i + 1];
                        sr += src[i + 2];
                        sa += src[i + 3];
                    }
                }
                var n = bw * bh;
                byte b = (byte)((sb + n / 2) / n), g = (byte)((sg + n / 2) / n), r = (byte)((sr + n / 2) / n), a = (byte)((sa + n / 2) / n);
                for (var y = 0; y < bh; y++)
                {
                    var o = (by + y) * dstStride + bx * 4;
                    for (var x = 0; x < bw; x++, o += 4)
                    {
                        dst[o] = b;
                        dst[o + 1] = g;
                        dst[o + 2] = r;
                        dst[o + 3] = a;
                    }
                }
            }
        }
        return result;
    }
}
