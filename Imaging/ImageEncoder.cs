using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Settings;

namespace AreaShot.Imaging;

/// <summary>PNG / JPEG への書き出し（仕様書 §12.3）。</summary>
public static class ImageEncoder
{
    public static void Encode(BitmapSource image, ImageFormatKind format, int jpegQuality, Stream output)
    {
        BitmapEncoder encoder;
        BitmapSource frame;
        if (format == ImageFormatKind.Jpeg)
        {
            encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
            // JPEG はアルファを持たない。明示的に 24bit へ変換する。
            frame = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        }
        else
        {
            encoder = new PngBitmapEncoder();
            // 画面キャプチャーは不透明。アルファを持たない 24bit で書き、ファイルを小さくする。
            frame = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        }
        encoder.Frames.Add(BitmapFrame.Create(frame));
        encoder.Save(output);
    }

    /// <summary>拡張子から画像形式を決める（名前を付けて保存）。.jpg / .jpeg 以外は PNG。</summary>
    public static ImageFormatKind FormatFromExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".jpe" ? ImageFormatKind.Jpeg : ImageFormatKind.Png;
    }
}
