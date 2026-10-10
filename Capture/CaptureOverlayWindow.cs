using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AreaShot.Core.Geometry;
using AreaShot.Core.Input;
using static AreaShot.Interop.NativeMethods;
using AreaShot.Core.Localization;

namespace AreaShot.Capture;

/// <summary>
/// 確定した範囲（仮想デスクトップ座標）。<see cref="ReleaseX"/> / <see cref="ReleaseY"/> はマウスを離した点、
/// <see cref="Action"/> は離すときに押していたキーに割り当てた操作（無ければ null = ダイアログを出す）。
/// </summary>
public readonly record struct AreaSelection(PixelRect Rect, int ReleaseX, int ReleaseY, QuickAction? Action);

/// <summary>
/// 範囲選択のオーバーレイ（仕様書 §5）。仮想デスクトップ全体を覆う 1 枚のウィンドウに、キャプチャー開始時の静止画を表示する。
///
/// 座標の扱い（仕様書 §16）:
/// - ウィンドウは物理ピクセルで仮想デスクトップと同じ位置・大きさに固定する（WM_WINDOWPOSCHANGING で上書き）。
///   拡大率の異なるモニターをまたいでも、WPF が WM_DPICHANGED でサイズを変えることはできない。
/// - 中身は「ウィンドウの DPI 倍率の逆数」で縮小して描き、描画単位 = 物理ピクセルにする（<see cref="PixelLayer"/>）。
/// - マウス位置は WPF の座標を使わず GetCursorPos（物理ピクセル）で取る。
/// これにより、表示している静止画・選択枠・切り出す範囲がすべて同じ物理ピクセル座標になり、ずれが生じない。
/// </summary>
public sealed class CaptureOverlayWindow : Window
{
    private readonly ScreenSnapshot _snapshot;
    private readonly SelectionLayer _selection;
    private readonly GuideLayer _guide;
    private readonly HintLayer? _hint;
    private readonly QuickActionSettings _quick;
    private IntPtr _hwnd;
    private bool _dragging;
    private int _x0, _y0;
    private bool _finished;
    private Cursor? _crosshair;

    public CaptureOverlayWindow(ScreenSnapshot snapshot, QuickActionSettings quickActions)
    {
        _snapshot = snapshot;
        _quick = quickActions.Clone();
        Title = Strings.OverlayTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 1;
        Height = 1;
        Cursor = Cursors.Cross; // 見やすい十字（CrosshairCursor）は DPI が決まる OnSourceInitialized で差し替える
        Focusable = true;

        _selection = new SelectionLayer(snapshot.Bounds.Width, snapshot.Bounds.Height);
        _guide = new GuideLayer(snapshot.Bounds.Width, snapshot.Bounds.Height);
        var root = new Grid();
        root.Children.Add(new ScreenLayer(snapshot));
        root.Children.Add(_selection);
        root.Children.Add(_guide);
        var assigned = _quick.Assigned().ToList();
        if (_quick.ShowHint && assigned.Count > 0)
        {
            _hint = new HintLayer(snapshot.Bounds, assigned);
            root.Children.Add(_hint);
        }
        Content = root;
    }

    /// <summary>範囲が確定した（仮想デスクトップ座標）。</summary>
    public event Action<AreaSelection>? Selected;

    /// <summary>キャンセルされた（Esc・右クリック・画面構成の変更など）。</summary>
    public event Action? Cancelled;

    public bool IsFinished => _finished;

    /// <summary>外部から中止する（モニター構成の変更など）。</summary>
    public void Cancel()
    {
        if (_finished) return;
        _finished = true;
        if (_dragging) ReleaseMouseCapture();
        Cancelled?.Invoke();
        Close();
    }

    /// <summary>最前面・入力対象にし直す（キャプチャー中にもう一度ショートカットが押された場合）。</summary>
    public void BringToFront()
    {
        if (_hwnd == IntPtr.Zero) return;
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        SetForegroundWindow(_hwnd);
        Activate();
        Keyboard.Focus(this);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Alt+Tab に出さない。
        var ex = GetWindowLongPtr(_hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW));
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        var b = _snapshot.Bounds;
        SetWindowPos(_hwnd, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, 0);
        _crosshair = CrosshairCursor.Create(VisualTreeHelper.GetDpi(this).DpiScaleX);
        Cursor = _crosshair;
        UpdateGuide(); // マウスを動かす前から表示する
        UpdateHint();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        BringToFront();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
            return;
        }
        UpdateHint();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        UpdateHint();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_finished) return;
        (_x0, _y0) = DisplayInfo.CursorPosition();
        _dragging = true;
        CaptureMouse();
        UpdateSelection();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateGuide();
        if (_dragging) UpdateSelection();
        UpdateHint();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging || _finished) return;
        var rect = CurrentRect();
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
        if (rect.IsEmpty)
        {
            // クリックだけ（幅または高さが 0）は選択し直し。
            _selection.SetSelection(null);
            return;
        }
        _finished = true;
        var (rx, ry) = DisplayInfo.CursorPosition();
        var action = _quick.Resolve(PressedKey());
        Close(); // 先に閉じる（「名前を付けて保存」の保存ダイアログなどがこの画面の下に隠れないように）
        Selected?.Invoke(new AreaSelection(rect, rx, ry, action));
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true;
        if (_dragging)
        {
            // ドラッグ中の右クリックは選択のやり直し。
            _dragging = false;
            ReleaseMouseCapture();
            _selection.SetSelection(null);
            UpdateHint();
            return;
        }
        Cancel();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging && !_finished)
        {
            // 他のウィンドウにマウスを奪われた等。選択をやり直せる状態に戻す。
            _dragging = false;
            _selection.SetSelection(null);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _crosshair?.Dispose();
        if (!_finished)
        {
            _finished = true;
            Cancelled?.Invoke();
        }
    }

    private PixelRect CurrentRect()
    {
        var (x1, y1) = DisplayInfo.CursorPosition();
        return SelectionMath.FromDrag(_x0, _y0, x1, y1, _snapshot.Bounds);
    }

    private void UpdateSelection() =>
        _selection.SetSelection(SelectionMath.ToImageSpace(CurrentRect(), _snapshot.Bounds));

    private void UpdateGuide()
    {
        var (x, y) = DisplayInfo.CursorPosition();
        _guide.SetPosition(x - _snapshot.Bounds.Left, y - _snapshot.Bounds.Top);
    }

    /// <summary>今押している修飾キー（WPF のキーボード状態 = このメッセージを受けた時点の状態）。</summary>
    private static ReleaseKey PressedKey()
    {
        var m = Keyboard.Modifiers;
        var k = KeyModifiers.None;
        if ((m & ModifierKeys.Control) != 0) k |= KeyModifiers.Control;
        if ((m & ModifierKeys.Shift) != 0) k |= KeyModifiers.Shift;
        if ((m & ModifierKeys.Alt) != 0) k |= KeyModifiers.Alt;
        if ((m & ModifierKeys.Windows) != 0) k |= KeyModifiers.Win;
        return ReleaseKeys.FromModifiers(k);
    }

    private void UpdateHint()
    {
        if (_hint is null) return;
        var (x, y) = DisplayInfo.CursorPosition();
        var b = _snapshot.Bounds;
        PixelRect? selection = _dragging ? SelectionMath.ToImageSpace(CurrentRect(), b) : null;
        _hint.SetState(x - b.Left, y - b.Top, selection, PressedKey());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_KEYMENU)
        {
            // Alt を押して離したときに Windows がメニュー操作の状態に入るのを止める（Alt を離すキーに使うため）。
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            var b = _snapshot.Bounds;
            if ((pos.flags & SWP_NOMOVE) == 0) { pos.x = b.Left; pos.y = b.Top; }
            if ((pos.flags & SWP_NOSIZE) == 0) { pos.cx = b.Width; pos.cy = b.Height; }
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    /// <summary>描画単位を物理ピクセルにする層の基底。WPF の DPI 倍率が変わったら描き直す。</summary>
    private abstract class PixelLayer : FrameworkElement
    {
        protected PixelLayer()
        {
            IsHitTestVisible = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        protected DpiScale Dpi => VisualTreeHelper.GetDpi(this);

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            InvalidateVisual();
        }

        protected sealed override void OnRender(DrawingContext dc)
        {
            var dpi = Dpi;
            dc.PushTransform(new ScaleTransform(1 / dpi.DpiScaleX, 1 / dpi.DpiScaleY));
            RenderPixels(dc, dpi);
            dc.Pop();
        }

        protected abstract void RenderPixels(DrawingContext dc, DpiScale dpi);
    }

    /// <summary>キャプチャー開始時の静止画（1 回だけ描く）。</summary>
    private sealed class ScreenLayer(ScreenSnapshot snapshot) : PixelLayer
    {
        protected override void RenderPixels(DrawingContext dc, DpiScale dpi) =>
            dc.DrawImage(snapshot.Image, new Rect(0, 0, snapshot.Bounds.Width, snapshot.Bounds.Height));
    }

    /// <summary>選択範囲外の暗幕・選択枠・サイズ表示（ドラッグ中だけ描き直す）。</summary>
    private sealed class SelectionLayer : PixelLayer
    {
        private static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)));
        private static readonly Brush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xD8, 0x20, 0x20, 0x20)));
        private static readonly Pen BorderPen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x9A, 0xFF))), 1));
        private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private readonly int _width;
        private readonly int _height;
        private PixelRect? _selection;

        public SelectionLayer(int width, int height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>選択範囲（ウィンドウ内の物理ピクセル座標）。null は未選択。</summary>
        public void SetSelection(PixelRect? selection)
        {
            if (_selection == selection) return;
            _selection = selection;
            InvalidateVisual();
        }

        protected override void RenderPixels(DrawingContext dc, DpiScale dpi)
        {
            if (_selection is not { } s || s.IsEmpty)
            {
                // 未選択: 全体を暗くする（キャプチャー待ちであることが分かるように）。
                dc.DrawRectangle(Dim, null, new Rect(0, 0, _width, _height));
                if (_selection is { } line) DrawSize(dc, dpi, line);
                return;
            }

            // 選択範囲の外側だけを 4 枚の矩形で覆う。範囲内は元の明るさのまま（塗らない）。
            dc.DrawRectangle(Dim, null, new Rect(0, 0, _width, s.Top));
            dc.DrawRectangle(Dim, null, new Rect(0, s.Bottom, _width, Math.Max(0, _height - s.Bottom)));
            dc.DrawRectangle(Dim, null, new Rect(0, s.Top, s.Left, s.Height));
            dc.DrawRectangle(Dim, null, new Rect(s.Right, s.Top, Math.Max(0, _width - s.Right), s.Height));

            // 枠は選択範囲の 1px 外側に描き、取得する画素に掛からないようにする。
            dc.DrawRectangle(null, BorderPen, new Rect(s.Left - 0.5, s.Top - 0.5, s.Width + 1, s.Height + 1));
            DrawSize(dc, dpi, s);
        }

        private void DrawSize(DrawingContext dc, DpiScale dpi, PixelRect s)
        {
            var text = new FormattedText(
                $"{s.Width} × {s.Height}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                LabelFace, 12 * dpi.DpiScaleY, Brushes.White, dpi.PixelsPerDip);
            var pad = 4 * dpi.DpiScaleX;
            var w = text.Width + pad * 2;
            var h = text.Height + pad;
            // 選択範囲の左上の外側（上）に出す。上に余白が無ければ範囲の内側に出す。
            var x = Math.Clamp(s.Left, 0, Math.Max(0, _width - w));
            var y = s.Top - h - 3 >= 0 ? s.Top - h - 3 : s.Top + 3;
            dc.DrawRoundedRectangle(LabelBackground, null, new Rect(x, y, w, h), 3, 3);
            dc.DrawText(text, new Point(x + pad, y + pad / 2));
        }

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }

    /// <summary>
    /// マウス位置を通る縦横のガイド線（画面の端まで）。開始位置を合わせやすくする（V1.0.3）。
    /// 白の実線に黒の破線を重ねた白黒交互の 1px 線で、明るい画面・暗い画面のどちらでも見える。
    /// 表示だけの層で、切り出す画像（開始時の静止画）には写らない。
    /// </summary>
    private sealed class GuideLayer : PixelLayer
    {
        private static readonly Pen WhitePen = Freeze(new Pen(Brushes.White, 1));
        private static readonly Pen BlackDashPen = Freeze(new Pen(Brushes.Black, 1) { DashStyle = Freeze(new DashStyle([4, 4], 0)) });

        private readonly int _width;
        private readonly int _height;
        private int _x = -1, _y = -1;

        public GuideLayer(int width, int height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>マウス位置（ウィンドウ内の物理ピクセル座標）。</summary>
        public void SetPosition(int x, int y)
        {
            if (x == _x && y == _y) return;
            _x = x;
            _y = y;
            InvalidateVisual();
        }

        protected override void RenderPixels(DrawingContext dc, DpiScale dpi)
        {
            if (_x < 0 || _y < 0 || _x >= _width || _y >= _height) return;
            // 画素の中心（+0.5）を通して、ちょうど 1 画素の幅に収める。
            var h0 = new Point(0, _y + 0.5);
            var h1 = new Point(_width, _y + 0.5);
            var v0 = new Point(_x + 0.5, 0);
            var v1 = new Point(_x + 0.5, _height);
            dc.DrawLine(WhitePen, h0, h1);
            dc.DrawLine(WhitePen, v0, v1);
            dc.DrawLine(BlackDashPen, h0, h1);
            dc.DrawLine(BlackDashPen, v0, v1);
        }

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }

    /// <summary>
    /// 「キーを押しながら離す」の案内（V1.0.4・仕様書 §5.4）。マウスがあるモニターの作業領域の下端中央に 1 行で出す。
    /// マウスか選択範囲が近づいたら上端へ逃げる。押しているキーに割り当てた操作だけを明るくし、離すと何が起きるかを示す。
    /// 表示だけの層で、切り出す画像には写らない。
    /// </summary>
    private sealed class HintLayer : PixelLayer
    {
        private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromArgb(0xC8, 0x20, 0x20, 0x20)));
        private static readonly Pen Border = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF))), 1));
        private static readonly Brush Normal = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)));
        private static readonly Brush Label = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8)));
        private static readonly Brush Dimmed = Freeze(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
        private static readonly Brush Active = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0xC8, 0xFF)));
        private static FontFamily Font => Views.UiFont.Family;

        private readonly PixelRect _bounds;
        private readonly IReadOnlyList<(QuickAction Action, ReleaseKey Key)> _items;
        private int _x = int.MinValue, _y = int.MinValue;
        private PixelRect? _selection;
        private ReleaseKey _pressed;

        /// <param name="bounds">仮想デスクトップ上のウィンドウの範囲（モニターの位置をウィンドウ座標へ直すため）。</param>
        public HintLayer(PixelRect bounds, IReadOnlyList<(QuickAction Action, ReleaseKey Key)> items)
        {
            _bounds = bounds;
            _items = items;
        }

        /// <summary>マウス位置・選択範囲（ウィンドウ内の物理ピクセル座標）と、押している修飾キー。</summary>
        public void SetState(int x, int y, PixelRect? selection, ReleaseKey pressed)
        {
            if (x == _x && y == _y && selection == _selection && pressed == _pressed) return;
            _x = x;
            _y = y;
            _selection = selection;
            _pressed = pressed;
            InvalidateVisual();
        }

        protected override void RenderPixels(DrawingContext dc, DpiScale dpi)
        {
            if (_x == int.MinValue) return;
            var monitor = DisplayInfo.MonitorAt(_x + _bounds.Left, _y + _bounds.Top);
            var work = monitor.WorkArea.Offset(-_bounds.Left, -_bounds.Top);
            var scale = monitor.DpiScale;
            var size = 12 * scale;
            var activeKey = _items.Any(i => i.Key == _pressed) ? _pressed : ReleaseKey.None;

            // 文字の並び（ラベル → 各操作）。押しているキーがあれば、その操作だけを明るくする。
            var runs = new List<FormattedText> { Text(Strings.OverlayHintLabel, Label, size, false, dpi) };
            foreach (var (action, key) in _items)
            {
                var on = key == activeKey;
                var brush = activeKey == ReleaseKey.None ? Normal : on ? Active : Dimmed;
                runs.Add(Text($"{ReleaseKeys.Describe(key)} {QuickActionSettings.ShortName(action)}", brush, size, on, dpi));
            }
            var padX = 12 * scale;
            var padY = 6 * scale;
            var gap = 16 * scale;
            var w = runs.Sum(r => r.WidthIncludingTrailingWhitespace) + gap * (runs.Count - 1) + padX * 2;
            var h = runs.Max(r => r.Height) + padY * 2;
            var margin = 24 * scale;

            var x = Math.Max(work.Left, work.Left + (work.Width - w) / 2);
            var bottom = new Rect(x, work.Bottom - margin - h, w, h);
            var top = new Rect(x, work.Top + margin, w, h);
            var box = !Blocked(bottom, margin) ? bottom
                : !Blocked(top, margin) ? top
                : !NearCursor(bottom, margin) ? bottom
                : top;

            // 1 画素の枠が画素に揃うよう、位置は整数に丸める。
            box = new Rect(Math.Round(box.X), Math.Round(box.Y), Math.Round(box.Width), Math.Round(box.Height));
            dc.DrawRoundedRectangle(Background, Border, new Rect(box.X + 0.5, box.Y + 0.5, box.Width - 1, box.Height - 1), 4 * scale, 4 * scale);
            var cx = box.X + padX;
            foreach (var run in runs)
            {
                dc.DrawText(run, new Point(cx, box.Y + (box.Height - run.Height) / 2));
                cx += run.WidthIncludingTrailingWhitespace + gap;
            }
        }

        /// <summary>マウスか選択範囲が、案内の周り（margin の幅）に掛かっている。</summary>
        private bool Blocked(Rect box, double margin)
        {
            if (NearCursor(box, margin)) return true;
            if (_selection is not { } s || s.IsEmpty) return false;
            var zone = Rect.Inflate(box, margin, margin);
            return zone.IntersectsWith(new Rect(s.Left, s.Top, s.Width, s.Height));
        }

        private bool NearCursor(Rect box, double margin) => Rect.Inflate(box, margin, margin).Contains(_x, _y);

        private static FormattedText Text(string text, Brush brush, double size, bool bold, DpiScale dpi) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
                size, brush, dpi.PixelsPerDip);

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }
    }
}
