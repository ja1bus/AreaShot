using AreaShot.Core.Geometry;
using static AreaShot.Interop.NativeMethods;

namespace AreaShot.Capture;

/// <summary>モニター情報（物理ピクセル）。ダイアログ・エディタを「キャプチャーした範囲のモニター」に出すために使う。</summary>
public static class DisplayInfo
{
    public readonly record struct MonitorArea(PixelRect Bounds, PixelRect WorkArea, double DpiScale);

    /// <summary>点（物理ピクセル）を含む、または最も近いモニター。</summary>
    public static MonitorArea MonitorAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            var vs = ScreenCapturer.VirtualScreenBounds();
            return new MonitorArea(vs, vs, SystemDpiScale);
        }
        var scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : SystemDpiScale;
        return new MonitorArea(ToPixelRect(info.rcMonitor), ToPixelRect(info.rcWork), scale);
    }

    /// <summary>システム DPI の倍率（WPF がウィンドウ作成前の Left / Top の換算に使う値）。</summary>
    public static double SystemDpiScale
    {
        get
        {
            try
            {
                var dpi = GetDpiForSystem();
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException)
            {
                return 1.0;
            }
        }
    }

    public static (int X, int Y) CursorPosition() =>
        GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    private static PixelRect ToPixelRect(RECT r) => PixelRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
}
