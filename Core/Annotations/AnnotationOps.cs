using System.Collections.Immutable;
using AreaShot.Core.Geometry;

namespace AreaShot.Core.Annotations;

/// <summary>文字の大きさを測る関数（WPF の FormattedText で測る。Core は描画系に依存しないため外から渡す）。</summary>
public delegate SizeD TextMeasurer(TextAnnotation text);

/// <summary>編集オブジェクトの一覧に対する操作・描画順・当たり判定（仕様書 §10・§11）。</summary>
public static class AnnotationOps
{
    public static ImmutableList<Annotation> Replace(this ImmutableList<Annotation> list, Annotation updated)
    {
        var index = list.FindIndex(a => a.Id == updated.Id);
        return index < 0 ? list : list.SetItem(index, updated);
    }

    public static ImmutableList<Annotation> Remove(this ImmutableList<Annotation> list, Guid id)
    {
        var index = list.FindIndex(a => a.Id == id);
        return index < 0 ? list : list.RemoveAt(index);
    }

    public static Annotation? Find(this ImmutableList<Annotation> list, Guid? id) =>
        id is null ? null : list.Find(a => a.Id == id.Value);

    /// <summary>
    /// 描画順（下から上）。モザイクを先にまとめて描き、その上に四角形・矢印・文字を追加順に描く。
    /// モザイクは元画像から生成するため、上に描くと先に描いた注釈を消してしまう。これを避ける（仕様書 §10.4）。
    /// </summary>
    public static IEnumerable<Annotation> InRenderOrder(IEnumerable<Annotation> list)
    {
        var items = list as IReadOnlyList<Annotation> ?? list.ToList();
        foreach (var a in items) if (a.Kind == AnnotationKind.Mosaic) yield return a;
        foreach (var a in items) if (a.Kind != AnnotationKind.Mosaic) yield return a;
    }

    /// <summary>オブジェクトの外接矩形（画像座標）。</summary>
    public static RectD BoundsOf(Annotation a, TextMeasurer measure) => a switch
    {
        RectangleAnnotation r => r.Bounds,
        MosaicAnnotation m => m.Bounds,
        ArrowAnnotation ar => RectD.FromPoints(ar.Start, ar.End),
        LineAnnotation l => RectD.FromPoints(l.Start, l.End),
        TextAnnotation t => TextBounds(t, measure(t)),
        _ => default,
    };

    private static RectD TextBounds(TextAnnotation t, SizeD size) => new(t.Position.X, t.Position.Y, size.Width, size.Height);

    /// <summary>点 <paramref name="p"/>（画像座標）がオブジェクトに当たるか。<paramref name="tolerance"/> は画像座標での許容幅。</summary>
    public static bool HitTest(Annotation a, PointD p, double tolerance, TextMeasurer measure) => a switch
    {
        RectangleAnnotation r => r.Bounds.Inflate(tolerance + r.StrokeWidth / 2).Contains(p),
        MosaicAnnotation m => m.Bounds.Inflate(tolerance).Contains(p),
        ArrowAnnotation ar => ArrowGeometry.DistanceToSegment(p, ar.Start, ar.End) <= tolerance + ar.StrokeWidth / 2,
        LineAnnotation l => ArrowGeometry.DistanceToSegment(p, l.Start, l.End) <= tolerance + l.StrokeWidth / 2,
        TextAnnotation t => BoundsOf(t, measure).Inflate(tolerance).Contains(p),
        _ => false,
    };

    /// <summary>最も上に描かれているオブジェクトから順に当たり判定し、最初に当たったものを返す。</summary>
    public static Annotation? HitTop(IEnumerable<Annotation> list, PointD p, double tolerance, TextMeasurer measure) =>
        InRenderOrder(list).Reverse().FirstOrDefault(a => HitTest(a, p, tolerance, measure));

    /// <summary>選択中オブジェクトのつまみ（変形用）の当たり判定。本体は含まない。</summary>
    public static HandleKind HitHandle(Annotation a, PointD p, double handleRadius)
    {
        switch (a)
        {
            case ArrowAnnotation ar:
                // 始点と終点が重なるほど短い場合は終点を優先する（伸ばせるように）。
                if (PointD.Distance(p, ar.End) <= handleRadius) return HandleKind.ArrowEnd;
                if (PointD.Distance(p, ar.Start) <= handleRadius) return HandleKind.ArrowStart;
                return HandleKind.None;
            case LineAnnotation l:
                // 直線の端点も矢印と同じつまみの種類で扱う。
                if (PointD.Distance(p, l.End) <= handleRadius) return HandleKind.ArrowEnd;
                if (PointD.Distance(p, l.Start) <= handleRadius) return HandleKind.ArrowStart;
                return HandleKind.None;
            case RectangleAnnotation or MosaicAnnotation:
                var bounds = a is RectangleAnnotation r ? r.Bounds : ((MosaicAnnotation)a).Bounds;
                foreach (var h in HandleMath.RectHandles)
                {
                    var hp = HandleMath.PositionOf(bounds, h);
                    if (Math.Abs(p.X - hp.X) <= handleRadius && Math.Abs(p.Y - hp.Y) <= handleRadius) return h;
                }
                return HandleKind.None;
            default:
                return HandleKind.None; // 文字はつまみで変形しない（サイズは文字サイズで変える）
        }
    }

    /// <summary>
    /// ドラッグ開始時のオブジェクト <paramref name="original"/> を、つまみ <paramref name="handle"/> で (dx, dy) 動かした結果。
    /// </summary>
    public static Annotation ApplyHandleDrag(Annotation original, HandleKind handle, double dx, double dy)
    {
        if (handle == HandleKind.Body) return original.MoveBy(dx, dy);
        return original switch
        {
            RectangleAnnotation r => r with { Bounds = HandleMath.Resize(r.Bounds, handle, dx, dy) },
            MosaicAnnotation m => m with { Bounds = HandleMath.Resize(m.Bounds, handle, dx, dy) },
            ArrowAnnotation ar when handle == HandleKind.ArrowStart => ar with { Start = ar.Start.Offset(dx, dy) },
            ArrowAnnotation ar when handle == HandleKind.ArrowEnd => ar with { End = ar.End.Offset(dx, dy) },
            LineAnnotation l when handle == HandleKind.ArrowStart => l with { Start = l.Start.Offset(dx, dy) },
            LineAnnotation l when handle == HandleKind.ArrowEnd => l with { End = l.End.Offset(dx, dy) },
            _ => original,
        };
    }

    /// <summary>
    /// ドラッグで作成中のオブジェクトの形を更新する（作成開始点 <paramref name="start"/> から現在点 <paramref name="current"/> まで）。
    /// </summary>
    public static Annotation ShapeFromDrag(Annotation created, PointD start, PointD current) => created switch
    {
        RectangleAnnotation r => r with { Bounds = RectD.FromPoints(start, current) },
        MosaicAnnotation m => m with { Bounds = RectD.FromPoints(start, current) },
        ArrowAnnotation ar => ar with { Start = start, End = current },
        LineAnnotation l => l with { Start = start, End = current },
        _ => created,
    };

    /// <summary>ドラッグで作ったオブジェクトが小さすぎる（クリックだけ等）か。</summary>
    public static bool IsDegenerate(Annotation a, double minSize) => a switch
    {
        RectangleAnnotation r => r.Bounds.Width < minSize || r.Bounds.Height < minSize,
        MosaicAnnotation m => m.Bounds.Width < minSize || m.Bounds.Height < minSize,
        ArrowAnnotation ar => PointD.Distance(ar.Start, ar.End) < minSize,
        LineAnnotation l => PointD.Distance(l.Start, l.End) < minSize,
        TextAnnotation t => string.IsNullOrWhiteSpace(t.Text),
        _ => true,
    };

    /// <summary>スタイルを変更した複製。対象外の項目は無視する。変更が無ければ同じインスタンスを返す。</summary>
    public static Annotation WithStyle(Annotation a, ArgbColor? color = null, double? strokeWidth = null, string? fontFamily = null, double? fontSize = null)
    {
        Annotation result = a switch
        {
            RectangleAnnotation r => r with { Color = color ?? r.Color, StrokeWidth = strokeWidth ?? r.StrokeWidth },
            ArrowAnnotation ar => ar with { Color = color ?? ar.Color, StrokeWidth = strokeWidth ?? ar.StrokeWidth },
            LineAnnotation l => l with { Color = color ?? l.Color, StrokeWidth = strokeWidth ?? l.StrokeWidth },
            TextAnnotation t => t with { Color = color ?? t.Color, FontFamily = fontFamily ?? t.FontFamily, FontSize = fontSize ?? t.FontSize },
            _ => a,
        };
        return result.Equals(a) ? a : result;
    }
}
