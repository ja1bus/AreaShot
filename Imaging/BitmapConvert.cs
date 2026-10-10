using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Imaging;

namespace AreaShot.Imaging;

/// <summary><see cref="PixelBuffer"/> と WPF の BitmapSource の相互変換。DPI は常に 96（1 ピクセル = 1 DIU）。</summary>
public static class BitmapConvert
{
    public const double Dpi = 96;

    /// <summary>Bgra32 の凍結済み BitmapSource を作る（画素はコピーされる）。</summary>
    public static BitmapSource ToBitmapSource(PixelBuffer buffer)
    {
        var bmp = BitmapSource.Create(buffer.Width, buffer.Height, Dpi, Dpi, PixelFormats.Bgra32, null, buffer.Pixels, buffer.Stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>任意の BitmapSource を Bgra32 の <see cref="PixelBuffer"/> にする。</summary>
    public static PixelBuffer ToPixelBuffer(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var buffer = PixelBuffer.Create(bgra.PixelWidth, bgra.PixelHeight);
        bgra.CopyPixels(buffer.Pixels, buffer.Stride, 0);
        return buffer;
    }

    /// <summary>範囲を切り出して Bgra32 の <see cref="PixelBuffer"/> にする。アルファは不透明に揃える（画面の画素に意味のあるアルファは無い）。</summary>
    public static PixelBuffer CropOpaque(BitmapSource source, Int32Rect rect)
    {
        var buffer = PixelBuffer.Create(rect.Width, rect.Height);
        BitmapSource bgra = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Bgr32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        bgra.CopyPixels(rect, buffer.Pixels, buffer.Stride, 0);
        var p = buffer.Pixels;
        for (var i = 3; i < p.Length; i += 4) p[i] = 255;
        return buffer;
    }

    /// <summary>
    /// 画面の Bgr32 画像から独立した切り出し画像を作る。一時画素はネイティブ領域に置き、作成後すぐ解放する。
    /// Bgr32 の未使用バイトは表示・エンコード時にアルファとして扱われず、PixelBuffer 化するときは不透明になる。
    /// </summary>
    public static BitmapSource CropOpaqueSource(BitmapSource source, Int32Rect rect)
    {
        if (source.Format != PixelFormats.Bgr32)
            throw new ArgumentException("The screen image must be in Bgr32 format.", nameof(source));

        var stride = checked(rect.Width * 4);
        var length = checked(stride * rect.Height);
        var bits = Marshal.AllocHGlobal(length);
        try
        {
            source.CopyPixels(rect, bits, length, stride);
            var bitmap = BitmapSource.Create(rect.Width, rect.Height, Dpi, Dpi, PixelFormats.Bgr32, null, bits, length, stride);
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            Marshal.FreeHGlobal(bits);
        }
    }
}
