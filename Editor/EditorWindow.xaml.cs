using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Capture;
using AreaShot.Core.Annotations;
using AreaShot.Core.Editing;
using AreaShot.Core.Geometry;
using AreaShot.Core.Settings;
using AreaShot.Imaging;
using AreaShot.Views;
using AreaShot.Core.Localization;

namespace AreaShot.Editor;

/// <summary>
/// 簡易イメージエディタ（仕様書 §9）。ツールバーの状態・文字入力・表示倍率・キーボード操作を受け持つ。
/// マウスでの作成・選択・変形は <see cref="EditorCanvas"/>、履歴は <see cref="EditHistory"/>、出力は AppHost が行う。
/// </summary>
public partial class EditorWindow : Window
{
    private sealed record ColorItem(string Name, ArgbColor Color, Brush Brush);

    // 色の名前は表示言語の文字列。画面ごとに作る。
    private readonly IReadOnlyList<ColorItem> _colorItems =
        EditorPresets.Colors.Select(c => new ColorItem(c.Name, c.Color, BrushCache.Get(c.Color))).ToList();

    private readonly CapturedImage _image;
    private readonly EditHistory _history = new();
    private readonly EditorCanvas _canvas;
    private readonly EditorDefaults _defaults;
    private bool _syncing;
    private double _zoom = 1;
    private bool _fit;

    // 文字の入力中の状態
    private TextBox? _textBox;
    private TextAnnotation? _editingText;
    private PointD _editingPosition;
    private (ArgbColor Color, string Font, double Size) _editingStyle;

    public EditorWindow(CapturedImage image, EditorDefaults defaults, string saveTooltip)
    {
        InitializeComponent();
        _image = image;
        _defaults = defaults.Clone();
        _defaults.FontFamily = FontCatalog.Resolve(_defaults.FontFamily);
        SaveButton.ToolTip = saveTooltip;

        _canvas = new EditorCanvas(image, _history);
        CanvasHost.Children.Insert(0, _canvas);
        _canvas.SelectionChanged += (_, _) => { SyncStyleControls(); UpdateCommandStates(); };
        _canvas.TextEditRequested += BeginTextEdit;
        // 文字の入力中にキャンバスをクリックしたら、確定だけ行う（そのクリックで新しい文字や図形を作らない）。
        _canvas.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (_textBox is null) return;
            CommitTextEdit();
            e.Handled = true;
        };
        _history.Changed += (_, _) => UpdateCommandStates();

        ColorBox.ItemsSource = _colorItems;
        WidthBox.ItemsSource = EditorPresets.StrokeWidths;
        FontSizeBox.ItemsSource = EditorPresets.FontSizes;
        FontBox.ItemsSource = FontCatalog.Available.Union([_defaults.FontFamily]).ToList();
        ZoomBox.ItemsSource = ZoomMath.Presets.Select(ZoomMath.Format).Append(Strings.EditorFit).ToList();

        ArgbColor.TryParse(_defaults.Color, out var color);
        _canvas.CurrentColor = color;
        _canvas.CurrentStrokeWidth = _defaults.StrokeWidth;

        RectangleTool.IsChecked = true; // 既定のツールは四角形（キャプチャー直後に注釈を描くことが多いため）
        SyncStyleControls();
        UpdateCommandStates();
        Loaded += (_, _) => ChooseInitialZoom();
    }

    /// <summary>出力（コピー・保存・名前を付けて保存）が選ばれた。成功したら AppHost が閉じる。</summary>
    public event Action<EditorWindow, CaptureAction>? OutputRequested;

    /// <summary>最後に使った色・線幅・フォント・文字サイズ（閉じるときに設定へ保存する）。</summary>
    public EditorDefaults Defaults => _defaults.Clone();

    public CapturedImage Image => _image;

    /// <summary>画像が 100% で収まる大きさのウィンドウにする（作業領域を超える分は WindowPlacement が縮める）。</summary>
    public void SizeToImage(double dpiScale)
    {
        Width = Math.Max(MinWidth, Math.Max(760, _image.Width / dpiScale + 60));
        Height = Math.Max(MinHeight, Math.Max(480, _image.Height / dpiScale + 190));
    }

    /// <summary>元画像と編集オブジェクトを合成した画像（元画像と同じピクセルサイズ）。</summary>
    public BitmapSource Compose()
    {
        PrepareOutput();
        return ComposePrepared();
    }

    /// <summary>保存ダイアログを開く前にも、文字入力とドラッグの確定だけは済ませる。</summary>
    public void PrepareOutput()
    {
        CommitTextEdit(focusCanvas: false);
        _canvas.CommitDrag();
    }

    /// <summary>確定済みの編集内容から画像を作る。</summary>
    public BitmapSource ComposePrepared() => ImageCompositor.Compose(_image.Source, _history.Current, _canvas.Mosaics);

    // ---- ツール・スタイル ----

    private void OnToolChecked(object sender, RoutedEventArgs e)
    {
        if (_canvas is null) return;
        CommitTextEdit(focusCanvas: false);
        _canvas.Tool = sender switch
        {
            _ when sender == SelectTool => EditTool.Select,
            _ when sender == LineTool => EditTool.Line,
            _ when sender == ArrowTool => EditTool.Arrow,
            _ when sender == TextTool => EditTool.Text,
            _ when sender == MosaicTool => EditTool.Mosaic,
            _ => EditTool.Rectangle,
        };
        SyncStyleControls();
        UpdateCommandStates();
    }

    /// <summary>
    /// スタイル欄の有効・無効と値を、対象（選択中のオブジェクト、無ければ現在のツール）に合わせる（仕様書 §9.2）。
    /// </summary>
    private void SyncStyleControls()
    {
        var selected = _canvas.Selected;
        AnnotationKind? kind = selected?.Kind ?? _canvas.Tool switch
        {
            EditTool.Rectangle => AnnotationKind.Rectangle,
            EditTool.Line => AnnotationKind.Line,
            EditTool.Arrow => AnnotationKind.Arrow,
            EditTool.Text => AnnotationKind.Text,
            EditTool.Mosaic => AnnotationKind.Mosaic,
            _ => null,
        };
        var support = StyleSupport.For(kind);
        SetEnabled(ColorBox, ColorLabel, support.Color);
        SetEnabled(WidthBox, WidthLabel, support.StrokeWidth);
        SetEnabled(FontBox, FontLabel, support.Font);
        SetEnabled(FontSizeBox, FontSizeLabel, support.FontSize);

        ArgbColor.TryParse(_defaults.Color, out var color);
        var width = _defaults.StrokeWidth;
        var font = _defaults.FontFamily;
        var size = _defaults.FontSize;
        switch (selected)
        {
            case RectangleAnnotation r: color = r.Color; width = r.StrokeWidth; break;
            case ArrowAnnotation a: color = a.Color; width = a.StrokeWidth; break;
            case LineAnnotation l: color = l.Color; width = l.StrokeWidth; break;
            case TextAnnotation t: color = t.Color; font = t.FontFamily; size = t.FontSize; break;
        }

        _syncing = true;
        try
        {
            ColorBox.SelectedItem = _colorItems.FirstOrDefault(c => c.Color == color);
            WidthBox.SelectedItem = EditorPresets.Nearest(EditorPresets.StrokeWidths, width);
            if (FontBox.ItemsSource is List<string> fonts && !fonts.Contains(font))
            {
                fonts.Add(font); // 以前の設定などで一覧に無いフォントを使っている文字
                FontBox.Items.Refresh();
            }
            FontBox.SelectedItem = font;
            FontSizeBox.SelectedItem = EditorPresets.Nearest(EditorPresets.FontSizes, size);
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SetEnabled(Control control, UIElement label, bool enabled)
    {
        control.IsEnabled = enabled;
        label.Opacity = enabled ? 1 : 0.4;
    }

    private void OnColorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || ColorBox.SelectedItem is not ColorItem item) return;
        _defaults.Color = item.Color.ToString();
        _canvas.CurrentColor = item.Color;
        ApplyToSelected(a => AnnotationOps.WithStyle(a, color: item.Color));
    }

    private void OnWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || WidthBox.SelectedItem is not double width) return;
        _defaults.StrokeWidth = width;
        _canvas.CurrentStrokeWidth = width;
        ApplyToSelected(a => AnnotationOps.WithStyle(a, strokeWidth: width));
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontBox.SelectedItem is not string font) return;
        _defaults.FontFamily = font;
        ApplyToSelected(a => AnnotationOps.WithStyle(a, fontFamily: font));
    }

    private void OnFontSizeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontSizeBox.SelectedItem is not double size) return;
        _defaults.FontSize = size;
        ApplyToSelected(a => AnnotationOps.WithStyle(a, fontSize: size));
    }

    /// <summary>選択中のオブジェクトのスタイルを変える（1 件の履歴。対象外の項目は WithStyle が無視する）。</summary>
    private void ApplyToSelected(Func<Annotation, Annotation> change)
    {
        CommitTextEdit(focusCanvas: false);
        if (_canvas.Selected is not { } selected) return;
        var updated = change(selected);
        if (!ReferenceEquals(updated, selected)) _history.Execute(_history.Current.Replace(updated));
    }

    // ---- 操作（Undo / Redo / 削除） ----

    private void UpdateCommandStates()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        DeleteButton.IsEnabled = _canvas.Selected is not null;
        ClearButton.IsEnabled = !_history.Current.IsEmpty;
    }

    private void Undo()
    {
        CommitTextEdit();
        if (_canvas.IsDragging) return;
        _history.Undo();
    }

    private void Redo()
    {
        CommitTextEdit();
        if (_canvas.IsDragging) return;
        _history.Redo();
    }

    private void DeleteSelected()
    {
        CommitTextEdit();
        if (_canvas.IsDragging || _canvas.Selected is not { } selected) return;
        _history.Execute(_history.Current.Remove(selected.Id));
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();
    private void OnDelete(object sender, RoutedEventArgs e) => DeleteSelected();

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        CommitTextEdit();
        _canvas.CommitDrag();
        _history.Execute([]); // 元画像は残る。Undo で戻せる。確認は出さない（仕様書 §9.6）。
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // 文字の入力中は TextBox に任せる（Enter / Esc は TextBox 側で処理する）。
        if (_textBox is not null && _textBox.IsKeyboardFocusWithin) return;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Z when ctrl && !shift:
                Undo();
                e.Handled = true;
                break;
            case Key.Y when ctrl:
            case Key.Z when ctrl && shift:
                Redo();
                e.Handled = true;
                break;
            case Key.Delete:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                if (!_canvas.CancelDrag()) _canvas.Select(null);
                e.Handled = true;
                break;
        }
    }

    // ---- 文字の入力・再編集 ----

    private void BeginTextEdit(TextAnnotation? existing, PointD position)
    {
        CommitTextEdit(focusCanvas: false);
        _editingText = existing;
        _editingPosition = existing?.Position ?? position;
        ArgbColor.TryParse(_defaults.Color, out var defaultColor);
        _editingStyle = existing is null
            ? (defaultColor, _defaults.FontFamily, _defaults.FontSize)
            : (existing.Color, existing.FontFamily, existing.FontSize);

        var scale = _canvas.View.Scale;
        var box = new TextBox
        {
            Text = existing?.Text ?? "",
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = FontCatalog.GetFamily(_editingStyle.Font),
            FontSize = Math.Max(1, _editingStyle.Size * scale),
            Foreground = BrushCache.Get(_editingStyle.Color),
            CaretBrush = Brushes.Black,
            Background = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 24,
        };
        // TextBox の枠（1）と内側の余白（左 2）の分だけずらし、確定後の描画位置と文字の位置を合わせる。
        Canvas.SetLeft(box, _editingPosition.X * scale - 3);
        Canvas.SetTop(box, _editingPosition.Y * scale - 1);
        box.PreviewKeyDown += OnTextBoxKeyDown;
        box.LostKeyboardFocus += (_, _) => CommitTextEdit(focusCanvas: false);
        _textBox = box;
        TextLayer.Children.Add(box);
        _canvas.HiddenId = existing?.Id;
        box.Loaded += (_, _) =>
        {
            box.Focus();
            box.CaretIndex = box.Text.Length;
        };
    }

    private void OnTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            CommitTextEdit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CommitTextEdit(cancel: true);
        }
    }

    /// <summary>文字の入力を確定する（空なら新規は作らず、既存は削除する）。<paramref name="cancel"/> は入力の破棄。</summary>
    private void CommitTextEdit(bool cancel = false, bool focusCanvas = true)
    {
        if (_textBox is not { } box) return;
        _textBox = null; // LostKeyboardFocus からの再入を防ぐ
        var existing = _editingText;
        _editingText = null;
        if (focusCanvas) _canvas.Focus();
        TextLayer.Children.Remove(box);
        _canvas.HiddenId = null;
        if (cancel) return;

        var text = box.Text.Replace("\r\n", "\n").TrimEnd('\n');
        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var created = new TextAnnotation(Guid.NewGuid(), _editingPosition, text, _editingStyle.Color, _editingStyle.Font, _editingStyle.Size);
            _history.Execute(_history.Current.Add(created));
            _canvas.Select(created.Id);
            return;
        }
        if (_history.Current.Find(existing.Id) is not TextAnnotation current) return;
        if (string.IsNullOrWhiteSpace(text))
            _history.Execute(_history.Current.Remove(current.Id));
        else if (text != current.Text)
            _history.Execute(_history.Current.Replace(current with { Text = text }));
    }

    // ---- 表示倍率 ----

    private void ChooseInitialZoom()
    {
        var fit = FitZoom();
        if (fit >= 1) SetZoom(1, fit: false);
        else SetZoom(fit, fit: true);
    }

    private double DpiScale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    private double FitZoom()
    {
        var dpi = DpiScale;
        // 余白（CanvasHost の Margin）とスクロールバーの分を除いた表示領域を物理ピクセルにする。
        var w = (Scroller.ActualWidth - 32 - SystemParameters.VerticalScrollBarWidth) * dpi;
        var h = (Scroller.ActualHeight - 32 - SystemParameters.HorizontalScrollBarHeight) * dpi;
        return ZoomMath.Fit(_image.Width, _image.Height, w, h);
    }

    private void SetZoom(double zoom, bool fit, Point? anchor = null)
    {
        CommitTextEdit(focusCanvas: false);
        var oldScale = _canvas.View.Scale;
        PointD? imagePoint = null;
        if (anchor is { } a)
        {
            var onCanvas = Scroller.TranslatePoint(a, _canvas);
            imagePoint = new PointD(onCanvas.X / oldScale, onCanvas.Y / oldScale);
        }

        _zoom = zoom;
        _fit = fit;
        var scale = ViewTransform.For(zoom, DpiScale).Scale;
        _canvas.SetScale(scale, zoom);

        if (imagePoint is { } ip && anchor is { } mouse)
        {
            // マウス位置の画像上の点が、拡大縮小後も同じ位置に来るようにスクロールする。
            Scroller.UpdateLayout();
            var now = _canvas.TranslatePoint(new Point(ip.X * scale, ip.Y * scale), Scroller);
            Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset + now.X - mouse.X);
            Scroller.ScrollToVerticalOffset(Scroller.VerticalOffset + now.Y - mouse.Y);
        }

        _syncing = true;
        try
        {
            var index = fit ? ZoomMath.Presets.Count : ZoomMath.Presets.ToList().FindIndex(p => Math.Abs(p - zoom) < 1e-6);
            ZoomBox.SelectedIndex = index;
        }
        finally
        {
            _syncing = false;
        }
        StatusText.Text = string.Format(Strings.EditorStatusFormat, _image.Width, _image.Height, ZoomMath.Format(zoom)) + (fit ? Strings.EditorStatusFitSuffix : "");
    }

    private void OnZoomSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _canvas is null || ZoomBox.SelectedIndex < 0) return;
        if (ZoomBox.SelectedIndex >= ZoomMath.Presets.Count) SetZoom(FitZoom(), fit: true);
        else SetZoom(ZoomMath.Presets[ZoomBox.SelectedIndex], fit: false);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        var next = e.Delta > 0 ? ZoomMath.StepIn(_zoom) : ZoomMath.StepOut(_zoom);
        if (Math.Abs(next - _zoom) > 1e-6 || _fit) SetZoom(next, fit: false, e.GetPosition(Scroller));
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fit && IsLoaded) SetZoom(FitZoom(), fit: true);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // 別の拡大率のモニターへ移動した。「100% = 1 画像ピクセル = 1 物理ピクセル」を保つ。
        if (IsLoaded) SetZoom(_fit ? FitZoom() : _zoom, _fit);
    }

    // ---- 出力 ----

    private void OnCopy(object sender, RoutedEventArgs e) => OutputRequested?.Invoke(this, CaptureAction.Copy);
    private void OnSave(object sender, RoutedEventArgs e) => OutputRequested?.Invoke(this, CaptureAction.Save);
    private void OnSaveAs(object sender, RoutedEventArgs e) => OutputRequested?.Invoke(this, CaptureAction.SaveAs);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
