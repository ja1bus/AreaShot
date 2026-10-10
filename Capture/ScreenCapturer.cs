using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Geometry;
using static AreaShot.Interop.NativeMethods;

namespace AreaShot.Capture;

/// <summary>仮想デスクトップ全体の静止画と、その範囲（物理ピクセルの仮想デスクトップ座標。負値あり）。</summary>
public sealed record ScreenSnapshot(BitmapSource Image, PixelRect Bounds);

/// <summary>
/// 画面の取得（仕様書 §5.1）。キャプチャー開始の瞬間に仮想デスクトップ全体を GDI の BitBlt で 1 回だけ取得する。
///
/// - プロセスは Per-Monitor V2 なので、GetSystemMetrics(SM_*VIRTUALSCREEN) と BitBlt は物理ピクセルで動く
///   （モニターごとの拡大率の違い・負の座標をそのまま扱える）。
/// - BitBlt はマウスカーソルを含まない（ソフトウェアカーソル環境を除く）。
/// - CAPTUREBLT は付けない（DWM 環境では付けなくてもレイヤードウィンドウは写る。付けるとカーソルがちらつく環境がある）。
/// </summary>
public static class ScreenCapturer
{
    public static PixelRect VirtualScreenBounds() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>仮想デスクトップ全体を取得する。取得できない場合（画面ロック中・セキュアデスクトップ等）は null。</summary>
    public static ScreenSnapshot? CaptureVirtualScreen()
    {
        var bounds = VirtualScreenBounds();
        if (bounds.IsEmpty) return null;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = IntPtr.Zero;
        var dib = IntPtr.Zero;
        var old = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == IntPtr.Zero) return null;
            var header = new BITMAPINFOHEADER
            {
                biSize = System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bounds.Width,
                biHeight = -bounds.Height, // 負 = 上の行から（トップダウン）
                biPlanes = 1,
                biBitCount = 32,
            };
            dib = CreateDIBSection(screenDc, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            old = SelectObject(memoryDc, dib);
            if (!BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.Left, bounds.Top, SRCCOPY))
                return null;

            var stride = bounds.Width * 4;
            var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null,
                bits, stride * bounds.Height, stride);
            image.Freeze();
            return new ScreenSnapshot(image, bounds);
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memoryDc, old);
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
