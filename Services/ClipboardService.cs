using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

/// <summary>
/// 画像のクリップボードへのコピー（仕様書 §12.1）。
/// 貼り付け先による差を減らすため、ビットマップ（CF_BITMAP / CF_DIB）と PNG の 2 形式を同時に置く。
/// 他のアプリがクリップボードを開いている間は失敗するため、短い間隔で数回やり直す。
/// </summary>
public static class ClipboardService
{
    private const int RetryCount = 10;
    private const int RetryDelayMs = 50;

    /// <summary>コピーする。失敗したらエラーメッセージを返す（成功なら null）。</summary>
    public static string? CopyImage(BitmapSource image)
    {
        DataObject data;
        try
        {
            // アルファ付きのまま置くと、一部のアプリで透明（黒）として貼られる。不透明の 32bit に揃える。
            var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            encoder.Save(png);
            data = new DataObject();
            data.SetImage(opaque);
            data.SetData("PNG", png, autoConvert: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            ErrorLog.Write("Failed to create the image for the clipboard", ex);
            return Strings.OutputImageFailed;
        }

        Exception? last = null;
        for (var i = 0; i < RetryCount; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return null;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                last = ex;
                Thread.Sleep(RetryDelayMs);
            }
        }
        ErrorLog.Write("Failed to copy to the clipboard", last);
        return Strings.ClipboardBusy;
    }
}
