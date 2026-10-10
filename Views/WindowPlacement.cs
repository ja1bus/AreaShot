using System.Windows;
using System.Windows.Interop;
using AreaShot.Capture;
using static AreaShot.Interop.NativeMethods;

namespace AreaShot.Views;

/// <summary>
/// ウィンドウを指定した点（物理ピクセル）のモニターの作業領域の中央、またはその点の近くに出す（仕様書 §16.4）。
/// WPF の Left / Top はモニターごとの拡大率が混在すると換算が分かりにくいため、
/// ウィンドウ作成直後（表示前）に物理ピクセルで位置を直す。
/// </summary>
public static class WindowPlacement
{
    /// <summary><see cref="Window.Show"/> の前に呼ぶ。Width / Height（DIU）は設定済みであること。</summary>
    public static void CenterOnMonitor(Window window, int anchorX, int anchorY)
    {
        var monitor = DisplayInfo.MonitorAt(anchorX, anchorY);
        var work = monitor.WorkArea;
        var scale = monitor.DpiScale;

        // 作業領域より大きい場合は縮める。
        if (window.Width * scale > work.Width) window.Width = work.Width / scale;
        if (window.Height * scale > work.Height) window.Height = work.Height / scale;

        // 作成前の見積もり（WPF はシステム DPI で換算する）。
        var sys = DisplayInfo.SystemDpiScale;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = (work.X + (work.Width - window.Width * scale) / 2) / sys;
        window.Top = (work.Y + (work.Height - window.Height * scale) / 2) / sys;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            Recenter(new WindowInteropHelper(window).Handle, work);
        }
        window.SourceInitialized += OnSourceInitialized;
    }

    /// <summary>
    /// <paramref name="target"/>（ウィンドウ内の要素）の中心が点（物理ピクセル）に重なるようにウィンドウを置く（仕様書 §16.4）。
    /// はみ出す場合は、その点のモニターの作業領域に収まるよう内側へずらす。<see cref="Window.Show"/> の前に呼ぶ。
    /// </summary>
    public static void PlaceNear(Window window, FrameworkElement target, int anchorX, int anchorY)
    {
        var monitor = DisplayInfo.MonitorAt(anchorX, anchorY);
        var work = monitor.WorkArea;
        var scale = monitor.DpiScale;
        if (window.Width * scale > work.Width) window.Width = work.Width / scale;
        if (window.Height * scale > work.Height) window.Height = work.Height / scale;

        // 作成前の見積もり: ウィンドウの中央を点に置く（同じモニターの拡大率で作られるように）。
        var sys = DisplayInfo.SystemDpiScale;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = (anchorX - window.Width * scale / 2) / sys;
        window.Top = (anchorY - window.Height * scale / 2) / sys;

        void OnSourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= OnSourceInitialized;
            var hwnd = new WindowInteropHelper(window).Handle;
            // 拡大率が変わると大きさも変わるので、測り直して 2 回目を合わせる。
            // 表示前は PointToScreen が使えないため、クライアント領域内の位置（DIU）×拡大率 + クライアント領域の左上で求める。
            for (var i = 0; i < 2; i++)
            {
                window.UpdateLayout();
                var client = new POINT();
                if (!GetWindowRect(hwnd, out var r) || !ClientToScreen(hwnd, ref client) || target.ActualWidth <= 0) return;
                var local = target.TransformToAncestor(window).Transform(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                var center = new Point(client.X + local.X * scale, client.Y + local.Y * scale);
                var w = r.Right - r.Left;
                var h = r.Bottom - r.Top;
                var x = anchorX - (int)Math.Round(center.X - r.Left);
                var y = anchorY - (int)Math.Round(center.Y - r.Top);
                x = Math.Max(work.Left, Math.Min(x, work.Right - w));
                y = Math.Max(work.Top, Math.Min(y, work.Bottom - h));
                if (x == r.Left && y == r.Top) return;
                SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
        }
        window.SourceInitialized += OnSourceInitialized;
    }

    private static void Recenter(IntPtr hwnd, Core.Geometry.PixelRect work)
    {
        // 別の拡大率のモニターへ動かすと WPF がサイズを変える（WM_DPICHANGED）ので、変わった大きさで 2 回目を合わせる。
        for (var i = 0; i < 2; i++)
        {
            if (!GetWindowRect(hwnd, out var r)) return;
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            var x = Math.Max(work.Left, work.Left + (work.Width - w) / 2);
            var y = Math.Max(work.Top, work.Top + (work.Height - h) / 2);
            if (x == r.Left && y == r.Top) return;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }
}
