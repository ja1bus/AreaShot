using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;

namespace AreaShot.Tests;

public class SelectionMathTests
{
    private static readonly PixelRect Desktop = new(-1920, -200, 1920 + 2560, 1440 + 200);

    [Fact]
    public void Drag_in_any_direction_gives_same_rect()
    {
        var a = SelectionMath.FromDrag(10, 20, 110, 70, Desktop);
        var b = SelectionMath.FromDrag(110, 70, 10, 20, Desktop);
        var c = SelectionMath.FromDrag(110, 20, 10, 70, Desktop);
        Assert.Equal(new PixelRect(10, 20, 100, 50), a);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void Negative_virtual_coordinates_and_monitor_crossing()
    {
        // 左のモニター（負の座標）から主モニターへまたぐ選択。
        var r = SelectionMath.FromDrag(-300, -100, 200, 50, Desktop);
        Assert.Equal(new PixelRect(-300, -100, 500, 150), r);
        Assert.Equal(new PixelRect(1620, 100, 500, 150), SelectionMath.ToImageSpace(r, Desktop));
    }

    [Fact]
    public void Points_are_clamped_to_virtual_desktop()
    {
        var r = SelectionMath.FromDrag(-5000, -5000, 100, 100, Desktop);
        Assert.Equal(Desktop.Left, r.Left);
        Assert.Equal(Desktop.Top, r.Top);
    }

    [Fact]
    public void Click_without_drag_is_empty()
    {
        Assert.True(SelectionMath.FromDrag(5, 5, 5, 5, Desktop).IsEmpty);
        Assert.True(SelectionMath.FromDrag(5, 5, 50, 5, Desktop).IsEmpty);
    }

    [Fact]
    public void Size_label_matches_captured_size()
    {
        var r = SelectionMath.FromDrag(0, 0, 1280, 720, Desktop);
        Assert.Equal((1280, 720), (r.Width, r.Height));
    }
}

public class ViewTransformTests
{
    [Theory]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(1.0, 1.5, 1 / 1.5)]
    [InlineData(0.5, 1.25, 0.4)]
    [InlineData(2.0, 2.0, 1.0)]
    public void Scale_is_zoom_divided_by_dpi(double zoom, double dpi, double expected)
    {
        Assert.Equal(expected, ViewTransform.For(zoom, dpi).Scale, 9);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void Image_to_view_roundtrip_is_exact_enough(double zoom)
    {
        var t = ViewTransform.For(zoom, 1.5);
        var p = new PointD(1234.5, 678.25);
        var back = t.ToImage(t.ToView(p));
        Assert.Equal(p.X, back.X, 9);
        Assert.Equal(p.Y, back.Y, 9);
    }

    [Fact]
    public void Same_image_point_regardless_of_zoom()
    {
        // 50% 表示でクリックした表示座標と 200% 表示でクリックした表示座標が、同じ画像上の点を指す。
        var image = new PointD(800, 450);
        var at50 = ViewTransform.For(0.5, 1.0);
        var at200 = ViewTransform.For(2.0, 1.0);
        Assert.Equal(new PointD(400, 225), at50.ToView(image));
        Assert.Equal(new PointD(1600, 900), at200.ToView(image));
        Assert.Equal(image, at50.ToImage(new PointD(400, 225)));
        Assert.Equal(image, at200.ToImage(new PointD(1600, 900)));
    }

    [Fact]
    public void Tolerance_in_image_space_shrinks_when_zoomed_in()
    {
        Assert.Equal(2.5, ViewTransform.For(2.0, 1.0).ToImageLength(5), 9);
        Assert.Equal(20, ViewTransform.For(0.25, 1.0).ToImageLength(5), 9);
    }
}

public class ZoomMathTests
{
    [Fact]
    public void Presets_match_spec()
    {
        Assert.Equal(new[] { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 }, ZoomMath.Presets);
    }

    [Theory]
    [InlineData(1.0, 1.25)]
    [InlineData(0.63, 0.75)]
    [InlineData(2.0, 2.0)]
    [InlineData(0.1, 0.25)]
    public void StepIn(double from, double expected) => Assert.Equal(expected, ZoomMath.StepIn(from));

    [Theory]
    [InlineData(1.0, 0.75)]
    [InlineData(0.63, 0.5)]
    [InlineData(0.25, 0.25)]
    [InlineData(3.0, 2.0)]
    public void StepOut(double from, double expected) => Assert.Equal(expected, ZoomMath.StepOut(from));

    [Fact]
    public void Fit_never_enlarges_beyond_100_percent()
    {
        Assert.Equal(1.0, ZoomMath.Fit(200, 100, 2000, 1000));
    }

    [Fact]
    public void Fit_uses_limiting_dimension()
    {
        Assert.Equal(0.5, ZoomMath.Fit(3840, 1000, 1920, 1000), 9);
        Assert.Equal(0.25, ZoomMath.Fit(1000, 4000, 1000, 1000), 9);
    }

    [Fact]
    public void Format_is_percent() => Assert.Equal("125%", ZoomMath.Format(1.25));
}

public class HandleMathTests
{
    private static readonly RectD R = new(100, 100, 200, 100);

    [Theory]
    [InlineData(HandleKind.TopLeft, 10, 20, 110, 120, 190, 80)]
    [InlineData(HandleKind.Top, 10, 20, 100, 120, 200, 80)]
    [InlineData(HandleKind.TopRight, 10, 20, 100, 120, 210, 80)]
    [InlineData(HandleKind.Right, 10, 20, 100, 100, 210, 100)]
    [InlineData(HandleKind.BottomRight, 10, 20, 100, 100, 210, 120)]
    [InlineData(HandleKind.Bottom, 10, 20, 100, 100, 200, 120)]
    [InlineData(HandleKind.BottomLeft, 10, 20, 110, 100, 190, 120)]
    [InlineData(HandleKind.Left, 10, 20, 110, 100, 190, 100)]
    [InlineData(HandleKind.Body, 10, 20, 110, 120, 200, 100)]
    public void Resize_each_handle(HandleKind h, double dx, double dy, double x, double y, double w, double hgt)
    {
        Assert.Equal(new RectD(x, y, w, hgt), HandleMath.Resize(R, h, dx, dy));
    }

    [Fact]
    public void Dragging_past_opposite_edge_flips_and_normalizes()
    {
        var r = HandleMath.Resize(R, HandleKind.Right, -250, 0); // 右辺を左辺より左へ
        Assert.Equal(new RectD(50, 100, 50, 100), r);
        Assert.True(r.Width >= 0 && r.Height >= 0);
    }

    [Fact]
    public void Handle_positions()
    {
        Assert.Equal(new PointD(100, 100), HandleMath.PositionOf(R, HandleKind.TopLeft));
        Assert.Equal(new PointD(200, 100), HandleMath.PositionOf(R, HandleKind.Top));
        Assert.Equal(new PointD(300, 150), HandleMath.PositionOf(R, HandleKind.Right));
        Assert.Equal(new PointD(300, 200), HandleMath.PositionOf(R, HandleKind.BottomRight));
        Assert.Equal(8, HandleMath.RectHandles.Count);
    }
}

public class ArrowGeometryTests
{
    [Fact]
    public void Head_is_at_end_and_scales_with_stroke_width()
    {
        var thin = ArrowGeometry.Compute(new PointD(0, 0), new PointD(200, 0), 1);
        var thick = ArrowGeometry.Compute(new PointD(0, 0), new PointD(200, 0), 8);
        Assert.True(thin.HasHead);
        Assert.Equal(new PointD(200, 0), thin.Tip);
        Assert.True(ArrowGeometry.HeadLength(8) > ArrowGeometry.HeadLength(1));
        // 羽は線に対して対称。
        Assert.Equal(thick.WingLeft.X, thick.WingRight.X, 9);
        Assert.Equal(-thick.WingLeft.Y, thick.WingRight.Y, 9);
        // 線は矢印の根元より手前で止まる（線の端が先端から出ない）。
        Assert.True(thick.LineEnd.X < thick.Tip.X);
        Assert.True(thick.LineEnd.X > thick.WingLeft.X);
    }

    [Fact]
    public void Short_arrow_head_does_not_go_behind_start()
    {
        var s = ArrowGeometry.Compute(new PointD(0, 0), new PointD(5, 0), 8);
        Assert.True(s.WingLeft.X >= 0 - 1e-9);
    }

    [Fact]
    public void Zero_length_has_no_head()
    {
        Assert.False(ArrowGeometry.Compute(new PointD(3, 3), new PointD(3, 3), 3).HasHead);
    }

    [Fact]
    public void Distance_to_segment()
    {
        Assert.Equal(5, ArrowGeometry.DistanceToSegment(new PointD(50, 5), new PointD(0, 0), new PointD(100, 0)), 9);
        Assert.Equal(5, ArrowGeometry.DistanceToSegment(new PointD(-3, 4), new PointD(0, 0), new PointD(100, 0)), 9);
    }
}

public class AnnotationOpsTests
{
    private static readonly TextMeasurer FixedMeasure = t => new SizeD(t.Text.Length * 10, t.FontSize);
    private static readonly ArgbColor Red = EditorPresets.DefaultColor;

    [Fact]
    public void Move_keeps_id_and_shifts_all_points()
    {
        var id = Guid.NewGuid();
        var arrow = new ArrowAnnotation(id, new PointD(0, 0), new PointD(10, 10), Red, 3);
        var moved = (ArrowAnnotation)arrow.MoveBy(5, -2);
        Assert.Equal(id, moved.Id);
        Assert.Equal(new PointD(5, -2), moved.Start);
        Assert.Equal(new PointD(15, 8), moved.End);
    }

    [Fact]
    public void Arrow_endpoints_are_independent_handles()
    {
        var arrow = new ArrowAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(100, 0), Red, 3);
        var a = (ArrowAnnotation)AnnotationOps.ApplyHandleDrag(arrow, HandleKind.ArrowStart, 10, 10);
        var b = (ArrowAnnotation)AnnotationOps.ApplyHandleDrag(arrow, HandleKind.ArrowEnd, 10, 10);
        Assert.Equal((new PointD(10, 10), new PointD(100, 0)), (a.Start, a.End));
        Assert.Equal((new PointD(0, 0), new PointD(110, 10)), (b.Start, b.End));
    }

    [Fact]
    public void Hit_handle_for_rectangle_and_arrow()
    {
        var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(10, 10, 100, 50), Red, 3);
        Assert.Equal(HandleKind.BottomRight, AnnotationOps.HitHandle(rect, new PointD(111, 59), 3));
        Assert.Equal(HandleKind.Top, AnnotationOps.HitHandle(rect, new PointD(60, 10), 3));
        Assert.Equal(HandleKind.None, AnnotationOps.HitHandle(rect, new PointD(60, 35), 3));

        var arrow = new ArrowAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(100, 0), Red, 3);
        Assert.Equal(HandleKind.ArrowStart, AnnotationOps.HitHandle(arrow, new PointD(1, 1), 3));
        Assert.Equal(HandleKind.ArrowEnd, AnnotationOps.HitHandle(arrow, new PointD(99, -1), 3));
    }

    [Fact]
    public void Hit_top_prefers_upper_object_and_mosaic_is_below_others()
    {
        var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 100));
        var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 100), Red, 3);
        // モザイクを後から追加しても、描画順ではモザイクが下なので四角形が当たる。
        var list = new Annotation[] { rect, mosaic };
        Assert.Same(rect, AnnotationOps.HitTop(list, new PointD(50, 50), 2, FixedMeasure));
        Assert.Equal(new Annotation[] { mosaic, rect }, AnnotationOps.InRenderOrder(list));
    }

    [Fact]
    public void Hit_test_text_uses_measured_bounds()
    {
        var text = new TextAnnotation(Guid.NewGuid(), new PointD(100, 100), "abcd", Red, "Meiryo", 20);
        Assert.True(AnnotationOps.HitTest(text, new PointD(139, 119), 0, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(text, new PointD(145, 119), 0, FixedMeasure));
    }

    [Fact]
    public void Hit_test_arrow_near_line_only()
    {
        var arrow = new ArrowAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(100, 100), Red, 2);
        Assert.True(AnnotationOps.HitTest(arrow, new PointD(52, 50), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(arrow, new PointD(80, 20), 2, FixedMeasure));
    }

    [Fact]
    public void Line_endpoints_hit_and_drag_like_arrow()
    {
        var line = new LineAnnotation(Guid.NewGuid(), new PointD(0, 0), new PointD(100, 100), Red, 2);
        Assert.True(AnnotationOps.HitTest(line, new PointD(52, 50), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(line, new PointD(80, 20), 2, FixedMeasure));
        Assert.Equal(HandleKind.ArrowStart, AnnotationOps.HitHandle(line, new PointD(1, 1), 3));
        Assert.Equal(HandleKind.ArrowEnd, AnnotationOps.HitHandle(line, new PointD(99, 101), 3));

        var moved = (LineAnnotation)AnnotationOps.ApplyHandleDrag(line, HandleKind.ArrowEnd, 10, -10);
        Assert.Equal((new PointD(0, 0), new PointD(110, 90)), (moved.Start, moved.End));
        var shaped = (LineAnnotation)AnnotationOps.ShapeFromDrag(line, new PointD(5, 5), new PointD(1, 9));
        Assert.Equal((new PointD(5, 5), new PointD(1, 9)), (shaped.Start, shaped.End));
        Assert.True(AnnotationOps.IsDegenerate(line with { End = new PointD(1, 1) }, 3));
        Assert.Equal(new RectD(0, 0, 100, 100), AnnotationOps.BoundsOf(line, FixedMeasure));
        var black = (LineAnnotation)AnnotationOps.WithStyle(line, color: EditorPresets.Colors[1].Color, strokeWidth: 8, fontSize: 48);
        Assert.Equal((EditorPresets.Colors[1].Color, 8.0), (black.Color, black.StrokeWidth));
    }

    [Fact]
    public void Shape_from_drag_normalizes_rect()
    {
        var created = new MosaicAnnotation(Guid.NewGuid(), default);
        var shaped = (MosaicAnnotation)AnnotationOps.ShapeFromDrag(created, new PointD(50, 50), new PointD(10, 20));
        Assert.Equal(new RectD(10, 20, 40, 30), shaped.Bounds);
    }

    [Fact]
    public void Degenerate_detection()
    {
        Assert.True(AnnotationOps.IsDegenerate(new RectangleAnnotation(Guid.NewGuid(), new RectD(0, 0, 2, 50), Red, 3), 3));
        Assert.False(AnnotationOps.IsDegenerate(new RectangleAnnotation(Guid.NewGuid(), new RectD(0, 0, 5, 5), Red, 3), 3));
        Assert.True(AnnotationOps.IsDegenerate(new TextAnnotation(Guid.NewGuid(), default, "  ", Red, "Meiryo", 20), 3));
    }

    [Fact]
    public void With_style_ignores_unsupported_and_returns_same_instance_when_unchanged()
    {
        var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 10, 10));
        Assert.Same(mosaic, AnnotationOps.WithStyle(mosaic, color: EditorPresets.Colors[1].Color, strokeWidth: 8));

        var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(0, 0, 10, 10), Red, 3);
        Assert.Same(rect, AnnotationOps.WithStyle(rect, color: Red, fontSize: 48));
        var black = (RectangleAnnotation)AnnotationOps.WithStyle(rect, color: EditorPresets.Colors[1].Color);
        Assert.Equal(EditorPresets.Colors[1].Color, black.Color);

        var text = new TextAnnotation(Guid.NewGuid(), default, "a", Red, "Meiryo", 20);
        var changed = (TextAnnotation)AnnotationOps.WithStyle(text, fontFamily: "MS Gothic", fontSize: 32, strokeWidth: 8);
        Assert.Equal(("MS Gothic", 32.0), (changed.FontFamily, changed.FontSize));
    }

    [Fact]
    public void Style_support_per_kind()
    {
        Assert.Equal(new StyleSupport(true, true, false, false), StyleSupport.For(AnnotationKind.Rectangle));
        Assert.Equal(new StyleSupport(true, true, false, false), StyleSupport.For(AnnotationKind.Arrow));
        Assert.Equal(new StyleSupport(true, true, false, false), StyleSupport.For(AnnotationKind.Line));
        Assert.Equal(new StyleSupport(true, false, true, true), StyleSupport.For(AnnotationKind.Text));
        Assert.Equal(StyleSupport.None, StyleSupport.For(AnnotationKind.Mosaic));
        Assert.Equal(StyleSupport.None, StyleSupport.For(null));
    }

    [Fact]
    public void Color_parse_and_format()
    {
        Assert.True(ArgbColor.TryParse("#FF112233", out var c));
        Assert.Equal(new ArgbColor(0xFF, 0x11, 0x22, 0x33), c);
        Assert.True(ArgbColor.TryParse("112233", out var c2));
        Assert.Equal(c, c2);
        Assert.Equal("#FF112233", c.ToString());
        Assert.False(ArgbColor.TryParse("red", out _));
    }

    [Fact]
    public void Presets_match_spec()
    {
        Assert.Equal(new double[] { 1, 2, 3, 5, 8 }, EditorPresets.StrokeWidths);
        Assert.Equal(new double[] { 12, 16, 20, 24, 32, 48 }, EditorPresets.FontSizes);
        Assert.Equal(EditorPresets.Colors[0].Color, EditorPresets.DefaultColor);
        // 色の名前は表示言語に従う（LocalizationTests でも確認）。
        using (new UiCultureScope("ja"))
        {
            Assert.Equal(new[] { "赤", "黒", "青", "緑", "黄", "白" }, EditorPresets.Colors.Select(c => c.Name));
        }
        using (new UiCultureScope("en"))
        {
            Assert.Equal(new[] { "Red", "Black", "Blue", "Green", "Yellow", "White" }, EditorPresets.Colors.Select(c => c.Name));
        }
    }
}
