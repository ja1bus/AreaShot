using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;

namespace AreaShot.Imaging;

/// <summary>
/// 編集オブジェクトの描画（画像座標で描く。表示時は呼び出し側が拡大縮小の変換を掛ける）。
/// 画面表示と出力の両方でこのクラスを使う。
/// </summary>
public static class AnnotationRenderer
{
    /// <summary>番号バッジの番号のフォント（固定。無ければ §10.5 の代替）。</summary>
    public const string BadgeFontFamily = "Segoe UI";

    /// <summary>
    /// <paramref name="annotations"/> を描画順（モザイクが下）に描く。<paramref name="skip"/> の Id は描かない（文字の編集中など）。
    /// 画像の外にはみ出した部分は切り取る。トリミングは描かない（表示はエディター、出力は切り出し）。
    /// </summary>
    public static void DrawAll(DrawingContext dc, IEnumerable<Annotation> annotations, MosaicCache mosaics, int imageWidth, int imageHeight, Guid? skip = null)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, imageWidth, imageHeight)));
        foreach (var a in AnnotationOps.InRenderOrder(annotations))
        {
            if (a.Id == skip) continue;
            Draw(dc, a, mosaics);
        }
        dc.Pop();
    }

    public static void Draw(DrawingContext dc, Annotation annotation, MosaicCache mosaics)
    {
        switch (annotation)
        {
            case RectangleAnnotation r:
                DrawRectangle(dc, r);
                break;
            case ArrowAnnotation a:
                DrawArrow(dc, a);
                break;
            case LineAnnotation l:
                DrawLine(dc, l);
                break;
            case TextAnnotation t:
                DrawText(dc, t);
                break;
            case BadgeAnnotation b:
                DrawBadge(dc, b);
                break;
            case HighlightAnnotation h:
                if (h.Bounds.Width > 0 && h.Bounds.Height > 0)
                    dc.DrawRectangle(BrushCache.Get(h.FillColor), null, new Rect(h.Bounds.X, h.Bounds.Y, h.Bounds.Width, h.Bounds.Height));
                break;
            case MosaicAnnotation m:
                var entry = mosaics.Get(m.Id, m.Bounds);
                if (entry is { } e)
                    dc.DrawImage(e.Bitmap, new Rect(e.Rect.X, e.Rect.Y, e.Rect.Width, e.Rect.Height));
                break;
        }
    }

    private static void DrawRectangle(DrawingContext dc, RectangleAnnotation r)
    {
        if (r.Bounds.Width <= 0 && r.Bounds.Height <= 0) return;
        var pen = new Pen(BrushCache.Get(r.Color), r.StrokeWidth) { LineJoin = PenLineJoin.Miter };
        pen.Freeze();
        dc.DrawRectangle(null, pen, new Rect(r.Bounds.X, r.Bounds.Y, r.Bounds.Width, r.Bounds.Height));
    }

    private static void DrawArrow(DrawingContext dc, ArrowAnnotation a)
    {
        var shape = ArrowGeometry.Compute(a.Start, a.End, a.StrokeWidth);
        var brush = BrushCache.Get(a.Color);
        var pen = new Pen(brush, a.StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Flat };
        pen.Freeze();
        if (!shape.HasHead)
        {
            dc.DrawEllipse(brush, null, new Point(a.Start.X, a.Start.Y), a.StrokeWidth / 2, a.StrokeWidth / 2);
            return;
        }
        if (shape.LineStart != shape.LineEnd)
            dc.DrawLine(pen, ToPoint(shape.LineStart), ToPoint(shape.LineEnd));

        var head = new StreamGeometry();
        using (var ctx = head.Open())
        {
            ctx.BeginFigure(ToPoint(shape.Tip), isFilled: true, isClosed: true);
            ctx.LineTo(ToPoint(shape.WingLeft), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(ToPoint(shape.WingRight), isStroked: false, isSmoothJoin: false);
        }
        head.Freeze();
        dc.DrawGeometry(brush, null, head);
    }

    private static void DrawLine(DrawingContext dc, LineAnnotation l)
    {
        var brush = BrushCache.Get(l.Color);
        if (l.Start == l.End)
        {
            dc.DrawEllipse(brush, null, ToPoint(l.Start), l.StrokeWidth / 2, l.StrokeWidth / 2);
            return;
        }
        var pen = new Pen(brush, l.StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.DrawLine(pen, ToPoint(l.Start), ToPoint(l.End));
    }

    /// <summary>文字。縁取りがあれば輪郭を太いペンで先に描き、その上に本体を描く（V1.4.0）。</summary>
    private static void DrawText(DrawingContext dc, TextAnnotation t)
    {
        var ft = TextLayout.Create(t);
        var origin = new Point(t.Position.X, t.Position.Y);
        if (t.Outline)
        {
            var thickness = TextOutlineStyle.Thickness(t.FontSize);
            // 線は輪郭の両側に出るため、外側に thickness だけ出るよう 2 倍の太さで描く。
            var pen = new Pen(BrushCache.Get(TextOutlineStyle.ColorFor(t.Color)), thickness * 2) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            var geometry = ft.BuildGeometry(origin);
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.DrawText(ft, origin);
    }

    /// <summary>番号バッジ。塗りつぶした円の中心に白の太字の番号を置く（V1.4.0）。</summary>
    private static void DrawBadge(DrawingContext dc, BadgeAnnotation b)
    {
        var r = BadgeGeometry.Radius(b.FontSize);
        if (!(r > 0)) return;
        var center = ToPoint(b.Center);
        dc.DrawEllipse(BrushCache.Get(b.Color), null, center, r, r);
        var typeface = new Typeface(FontCatalog.GetFamily(BadgeFontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var ft = new FormattedText(b.Number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, Math.Max(1, b.FontSize), Brushes.White, pixelsPerDip: 1.0);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private static Point ToPoint(PointD p) => new(p.X, p.Y);
}
