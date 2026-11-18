using System.Collections.Immutable;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AreaShot.Capture;
using AreaShot.Core.Annotations;
using AreaShot.Core.Editing;
using AreaShot.Core.Geometry;
using AreaShot.Imaging;

namespace AreaShot.Editor;

/// <summary>エディタのツール（仕様書 §9.2）。移動は選択ツールの操作。</summary>
public enum EditTool
{
    Select,
    Line,
    Rectangle,
    Arrow,
    Text,
    Mosaic,
    /// <summary>番号バッジ（V1.4.0）。</summary>
    Badge,
    /// <summary>ハイライト（V1.4.0）。</summary>
    Highlight,
    /// <summary>トリミング（V1.4.0）。</summary>
    Crop,
}

/// <summary>
/// 編集キャンバス（仕様書 §9）。元画像と編集オブジェクトを表示し、マウス操作で作成・選択・移動・変形する。
///
/// - 編集オブジェクトは画像座標で保持し、表示時だけ <see cref="ViewTransform"/> で拡大縮小する。
///   マウス位置は表示座標 → 画像座標へ戻してから扱うため、表示倍率を変えても位置がずれない。
/// - ドラッグ中は <see cref="EditHistory.Preview"/> で途中経過を表示し、離したときに 1 件の履歴として確定する。
/// - 描画は <see cref="AnnotationRenderer"/>（出力と同じ）。モザイクは <see cref="MosaicCache"/> で該当範囲だけ作り直す。
/// </summary>
public sealed class EditorCanvas : FrameworkElement
{
    /// <summary>つまみの大きさ（DIU、表示倍率に関係なく一定）。</summary>
    private const double HandleSize = 9;
    /// <summary>当たり判定の余裕（DIU）。</summary>
    private const double HitTolerance = 5;
    /// <summary>これより小さく作ったオブジェクトはクリックとみなして捨てる（DIU）。</summary>
    private const double MinCreateSize = 4;

    private static readonly Pen SelectionPen = CreateSelectionPen();
    private static readonly Pen HandlePen = Freeze(new Pen(Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8))), 1));
    private static readonly Brush HandleFill = Brushes.White;
    /// <summary>トリミングの範囲の外の暗幕。</summary>
    private static readonly Brush CropShade = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
    private static readonly Pen CropFrameWhite = Freeze(new Pen(Brushes.White, 1));
    private static readonly Pen CropFrameDark = Freeze(new Pen(Brushes.Black, 1) { DashStyle = new DashStyle([4, 4], 0) });

    private readonly CapturedImage _image;
    private readonly MosaicCache _mosaics;
    private readonly EditHistory _history;
    private ViewTransform _view = new(1);
    private EditTool _tool = EditTool.Rectangle;
    private Guid? _selectedId;
    private Guid? _hiddenId;

    private enum DragMode { None, Create, Transform }
    private DragMode _drag;
    private Annotation? _dragOriginal;
    private HandleKind _dragHandle;
    private PointD _dragStart;
    private ImmutableList<Annotation> _dragBase = [];

    public EditorCanvas(CapturedImage image, EditHistory history)
    {
        _image = image;
        _history = history;
        _mosaics = new MosaicCache(() => image.Pixels, image.Width, image.Height);
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        // 単キーでツールを切り替えるため、キャンバスでは IME を使わない（文字の入力は別の TextBox。V1.4.0）。
        InputMethod.SetIsInputMethodEnabled(this, false);
        _history.Changed += OnHistoryChanged;
        UpdateScalingMode(1);
    }

    /// <summary>選択が変わった。</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>文字の入力・再編集を依頼する（既存の文字なら null 以外）。</summary>
    public event Action<TextAnnotation?, PointD>? TextEditRequested;

    /// <summary>新規オブジェクトの色・線幅（エディタのツールバーの値）。</summary>
    public ArgbColor CurrentColor { get; set; } = EditorPresets.DefaultColor;
    public double CurrentStrokeWidth { get; set; } = EditorPresets.DefaultStrokeWidth;

    /// <summary>新規の番号バッジの文字サイズ（V1.4.0）。</summary>
    public double CurrentFontSize { get; set; } = EditorPresets.DefaultFontSize;

    public ViewTransform View => _view;
    public MosaicCache Mosaics => _mosaics;
    public bool IsDragging => _drag != DragMode.None;

    public EditTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            FinishDrag(commit: true);
            _tool = value;
            // 描画ツールへ切り替えたら選択を外す。トリミングは既存の範囲を選んで、つまみをすぐ出す（V1.4.0）。
            if (value == EditTool.Crop) Select(AnnotationOps.CropOf(_history.Current)?.Id);
            else if (value != EditTool.Select) Select(null);
            Cursor = value == EditTool.Select ? Cursors.Arrow : Cursors.Cross;
        }
    }

    public Annotation? Selected => _history.Current.Find(_selectedId);

    /// <summary>表示しない（文字の編集中に TextBox と重ならないように）オブジェクト。</summary>
    public Guid? HiddenId
    {
        get => _hiddenId;
        set
        {
            _hiddenId = value;
            InvalidateVisual();
        }
    }

    /// <summary>表示倍率（1 画像ピクセルあたりの DIU）を設定する。</summary>
    public void SetScale(double scale, double zoom)
    {
        if (scale <= 0 || double.IsNaN(scale)) return;
        _view = new ViewTransform(scale);
        UpdateScalingMode(zoom);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Select(Guid? id)
    {
        if (_selectedId == id) return;
        _selectedId = id;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ドラッグ中なら取り消す（Esc）。取り消した場合 true。</summary>
    public bool CancelDrag()
    {
        if (_drag == DragMode.None) return false;
        FinishDrag(commit: false);
        return true;
    }

    /// <summary>ドラッグ中なら確定する（出力・Undo の前）。</summary>
    public void CommitDrag() => FinishDrag(commit: true);

    protected override Size MeasureOverride(Size availableSize) =>
        new(_image.Width * _view.Scale, _image.Height * _view.Scale);

    protected override Size ArrangeOverride(Size finalSize) => MeasureOverride(finalSize);

    protected override void OnRender(DrawingContext dc)
    {
        var s = _view.Scale;
        dc.PushTransform(new ScaleTransform(s, s));
        dc.DrawImage(_image.Source, new Rect(0, 0, _image.Width, _image.Height));
        AnnotationRenderer.DrawAll(dc, _history.Current, _mosaics, _image.Width, _image.Height, _hiddenId);
        dc.Pop();

        DrawCrop(dc);
        if (Selected is { } selected && selected.Id != _hiddenId) DrawSelection(dc, selected);
    }

    /// <summary>
    /// トリミングの範囲の外を暗幕で覆い、範囲に枠を描く（V1.4.0）。範囲は出力と同じ整数ピクセル・画像内（<see cref="CropMath"/>）。
    /// 空（画像の外など）なら何も描かない（出力も全体になる）。
    /// </summary>
    private void DrawCrop(DrawingContext dc)
    {
        if (CropMath.EffectiveRect(_history.Current, _image.Width, _image.Height) is not { } r) return;
        var s = _view.Scale;
        double w = _image.Width * s, h = _image.Height * s;
        double l = r.Left * s, t = r.Top * s, ri = r.Right * s, b = r.Bottom * s;
        dc.DrawRectangle(CropShade, null, new Rect(0, 0, w, t));
        dc.DrawRectangle(CropShade, null, new Rect(0, b, w, Math.Max(0, h - b)));
        dc.DrawRectangle(CropShade, null, new Rect(0, t, l, Math.Max(0, b - t)));
        dc.DrawRectangle(CropShade, null, new Rect(ri, t, Math.Max(0, w - ri), Math.Max(0, b - t)));
        var frame = new Rect(l + 0.5, t + 0.5, Math.Max(0, ri - l - 1), Math.Max(0, b - t - 1));
        dc.DrawRectangle(null, CropFrameWhite, frame);
        dc.DrawRectangle(null, CropFrameDark, frame);
    }

    /// <summary>選択中のオブジェクトを画像座標で (dx, dy) 動かす（矢印キー。1 件の履歴）。動かした場合 true（V1.4.0）。</summary>
    public bool MoveSelected(double dx, double dy)
    {
        if (_drag != DragMode.None || Selected is not { } selected) return false;
        var moved = AnnotationOps.ApplyHandleDrag(selected, HandleKind.Body, dx, dy);
        _history.Execute(_history.Current.Replace(moved));
        return true;
    }

    private void DrawSelection(DrawingContext dc, Annotation a)
    {
        var bounds = _view.ToView(AnnotationOps.BoundsOf(a, TextLayout.Measurer));
        switch (a)
        {
            case ArrowAnnotation or LineAnnotation:
                var (start, end) = a is ArrowAnnotation ar ? (ar.Start, ar.End) : (((LineAnnotation)a).Start, ((LineAnnotation)a).End);
                foreach (var p in new[] { start, end })
                {
                    var v = _view.ToView(p);
                    dc.DrawEllipse(HandleFill, HandlePen, new Point(v.X, v.Y), HandleSize / 2, HandleSize / 2);
                }
                break;
            case RectangleAnnotation or MosaicAnnotation or HighlightAnnotation or CropAnnotation:
                dc.DrawRectangle(null, SelectionPen, ToRect(bounds));
                var rectBounds = AnnotationOps.RectBoundsOf(a) ?? default;
                foreach (var h in HandleMath.RectHandles)
                {
                    var v = _view.ToView(HandleMath.PositionOf(rectBounds, h));
                    dc.DrawRectangle(HandleFill, HandlePen, new Rect(v.X - HandleSize / 2, v.Y - HandleSize / 2, HandleSize, HandleSize));
                }
                break;
            case TextAnnotation:
                dc.DrawRectangle(null, SelectionPen, ToRect(bounds.Inflate(2)));
                break;
            case BadgeAnnotation:
                // 番号はつまみで変形しない（大きさは文字サイズ）。外接正方形の破線だけ。
                dc.DrawRectangle(null, SelectionPen, ToRect(bounds.Inflate(2)));
                break;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        e.Handled = true;
        if (_drag != DragMode.None) return;
        var p = ToImage(e.GetPosition(this));
        var tolerance = _view.ToImageLength(HitTolerance);

        // ダブルクリックで文字を再編集（どのツールでも）。
        if (e.ClickCount == 2 && AnnotationOps.HitTop(_history.Current, p, tolerance, TextLayout.Measurer) is TextAnnotation text)
        {
            Select(text.Id);
            TextEditRequested?.Invoke(text, text.Position);
            return;
        }

        // 選択中オブジェクトのつまみ（どのツールでも操作できる）。
        if (Selected is { } selected)
        {
            var handle = AnnotationOps.HitHandle(selected, p, _view.ToImageLength(HandleSize / 2 + 2));
            if (handle != HandleKind.None)
            {
                BeginTransform(selected, handle, p);
                return;
            }
        }

        switch (_tool)
        {
            case EditTool.Select:
                var hit = AnnotationOps.HitTop(_history.Current, p, tolerance, TextLayout.Measurer);
                Select(hit?.Id);
                if (hit is not null) BeginTransform(hit, HandleKind.Body, p);
                break;
            case EditTool.Text:
                if (AnnotationOps.HitTop(_history.Current, p, tolerance, TextLayout.Measurer) is TextAnnotation existing)
                {
                    Select(existing.Id);
                    BeginTransform(existing, HandleKind.Body, p);
                }
                else
                {
                    Select(null);
                    TextEditRequested?.Invoke(null, Clamp(p));
                }
                break;
            case EditTool.Badge:
                // ダブルクリックの 2 回目では作らない（同じ場所に重ならないように）。
                if (e.ClickCount < 2) BeginBadge(Clamp(p));
                break;
            default:
                BeginCreate(Clamp(p));
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = ToImage(e.GetPosition(this));
        switch (_drag)
        {
            case DragMode.Create when _dragOriginal is not null:
                var shaped = AnnotationOps.ShapeFromDrag(_dragOriginal, _dragStart, Clamp(p));
                _history.Preview(_dragBase.Replace(shaped));
                break;
            case DragMode.Transform when _dragOriginal is not null:
                // 変形（つまみ）は画像の範囲内へ、移動は自由（はみ出た部分は描画時に切り取る）。
                var q = _dragHandle == HandleKind.Body ? p : Clamp(p);
                var moved = AnnotationOps.ApplyHandleDrag(_dragOriginal, _dragHandle, q.X - _dragStart.X, q.Y - _dragStart.Y);
                _history.Preview(_dragBase.Replace(moved));
                break;
            default:
                UpdateHoverCursor(p);
                break;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_drag != DragMode.None)
        {
            e.Handled = true;
            FinishDrag(commit: true);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        FinishDrag(commit: true);
    }

    private void BeginCreate(PointD p)
    {
        var id = Guid.NewGuid();
        Annotation created = _tool switch
        {
            EditTool.Rectangle => new RectangleAnnotation(id, new RectD(p.X, p.Y, 0, 0), CurrentColor, CurrentStrokeWidth),
            EditTool.Arrow => new ArrowAnnotation(id, p, p, CurrentColor, CurrentStrokeWidth),
            EditTool.Line => new LineAnnotation(id, p, p, CurrentColor, CurrentStrokeWidth),
            EditTool.Highlight => new HighlightAnnotation(id, new RectD(p.X, p.Y, 0, 0), CurrentColor),
            EditTool.Crop => new CropAnnotation(id, new RectD(p.X, p.Y, 0, 0)),
            _ => new MosaicAnnotation(id, new RectD(p.X, p.Y, 0, 0)),
        };
        Select(null);
        _history.BeginPreview();
        // トリミングは一覧に最大 1 つ。既存の範囲は置き換える（小さすぎて捨てた場合は CancelPreview で元に戻る）。
        _dragBase = created is CropAnnotation crop ? _history.Current.ReplaceCrop(crop) : _history.Current.Add(created);
        _history.Preview(_dragBase);
        _drag = DragMode.Create;
        _dragOriginal = created;
        _dragStart = p;
        CaptureMouse();
    }

    /// <summary>
    /// 番号バッジをクリックした位置に置き、そのまま押したままのドラッグで動かせるようにする（作成と移動を 1 件の履歴にまとめる。V1.4.0）。
    /// </summary>
    private void BeginBadge(PointD p)
    {
        var badge = new BadgeAnnotation(Guid.NewGuid(), p, AnnotationOps.NextBadgeNumber(_history.Current), CurrentColor, CurrentFontSize);
        _history.BeginPreview();
        _dragBase = _history.Current.Add(badge);
        _history.Preview(_dragBase);
        Select(badge.Id);
        _drag = DragMode.Transform;
        _dragOriginal = badge;
        _dragHandle = HandleKind.Body;
        _dragStart = p;
        CaptureMouse();
    }

    private void BeginTransform(Annotation target, HandleKind handle, PointD p)
    {
        _history.BeginPreview();
        _dragBase = _history.Current;
        _drag = DragMode.Transform;
        _dragOriginal = target;
        _dragHandle = handle;
        _dragStart = p;
        CaptureMouse();
    }

    private void FinishDrag(bool commit)
    {
        if (_drag == DragMode.None) return;
        var mode = _drag;
        var created = _dragOriginal;
        _drag = DragMode.None;
        _dragOriginal = null;
        if (IsMouseCaptured) ReleaseMouseCapture(); // OnLostMouseCapture から再入しても _drag は None

        if (!commit)
        {
            _history.CancelPreview();
            return;
        }
        if (mode == DragMode.Create && created is not null)
        {
            var final = _history.Current.Find(created.Id);
            if (final is null || AnnotationOps.IsDegenerate(final, _view.ToImageLength(MinCreateSize)))
            {
                _history.CancelPreview();
                return;
            }
            _history.CommitPreview();
            Select(final.Id);
            return;
        }
        _history.CommitPreview();
    }

    private void UpdateHoverCursor(PointD p)
    {
        if (Selected is { } selected)
        {
            var handle = AnnotationOps.HitHandle(selected, p, _view.ToImageLength(HandleSize / 2 + 2));
            if (handle != HandleKind.None)
            {
                Cursor = CursorFor(handle);
                return;
            }
        }
        if (_tool is EditTool.Select or EditTool.Text &&
            AnnotationOps.HitTop(_history.Current, p, _view.ToImageLength(HitTolerance), TextLayout.Measurer) is { } hit &&
            (_tool == EditTool.Select || hit is TextAnnotation))
        {
            Cursor = Cursors.SizeAll;
            return;
        }
        Cursor = _tool switch
        {
            EditTool.Select => Cursors.Arrow,
            EditTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }

    private static Cursor CursorFor(HandleKind h) => h switch
    {
        HandleKind.TopLeft or HandleKind.BottomRight => Cursors.SizeNWSE,
        HandleKind.TopRight or HandleKind.BottomLeft => Cursors.SizeNESW,
        HandleKind.Top or HandleKind.Bottom => Cursors.SizeNS,
        HandleKind.Left or HandleKind.Right => Cursors.SizeWE,
        HandleKind.ArrowStart or HandleKind.ArrowEnd => Cursors.Cross,
        _ => Cursors.SizeAll,
    };

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // Undo 等で選択中のオブジェクトが無くなったら選択を外す。
        if (_selectedId is not null && _history.Current.Find(_selectedId) is null) Select(null);
        if (!_history.IsPreviewing) _mosaics.Prune(_history.Current.Select(a => a.Id).ToHashSet());
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty); // 選択中オブジェクトのスタイル表示を更新する
    }

    private void UpdateScalingMode(double zoom) =>
        // 拡大（100% 以上）は画素をそのまま拡大して見せ、縮小は滑らかに縮める。
        RenderOptions.SetBitmapScalingMode(this, zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

    private PointD ToImage(Point view) => _view.ToImage(new PointD(view.X, view.Y));

    private PointD Clamp(PointD p) => new(Math.Clamp(p.X, 0, _image.Width), Math.Clamp(p.Y, 0, _image.Height));

    private static Rect ToRect(RectD r) => new(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));

    private static Pen CreateSelectionPen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7)), 1) { DashStyle = new DashStyle([4, 3], 0) };
        pen.Freeze();
        return pen;
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
