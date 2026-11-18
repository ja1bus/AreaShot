using System.Collections.Immutable;
using AreaShot.Core.Annotations;
using AreaShot.Core.Editing;
using AreaShot.Core.Geometry;

namespace AreaShot.Tests;

/// <summary>V1.4.0 の注釈（番号バッジ・ハイライト・トリミング・文字の縁取り）のモデルと操作（verification.md T-01〜T-07・T-11・T-25・T-29・T-30・T-35〜T-37）。</summary>
public class V140AnnotationTests
{
    private static readonly ArgbColor Red = ArgbColor.FromRgb(0xE6, 0x00, 0x12);
    private static readonly ArgbColor Black = ArgbColor.FromRgb(0, 0, 0);
    private static readonly ArgbColor White = ArgbColor.FromRgb(255, 255, 255);
    private static readonly TextMeasurer FixedMeasure = _ => new SizeD(40, 20);

    private static BadgeAnnotation Badge(int number, double x = 10, double y = 10, double size = 20) =>
        new(Guid.NewGuid(), new PointD(x, y), number, Red, size);

    private static RectangleAnnotation Rect(double x, double y, double w, double h) =>
        new(Guid.NewGuid(), new RectD(x, y, w, h), Red, 3);

    // ---- 番号バッジ（AC-1） ----

    [Fact]
    public void T01_next_badge_number_is_max_plus_one()
    {
        Assert.Equal(1, AnnotationOps.NextBadgeNumber([]));
        Assert.Equal(1, AnnotationOps.NextBadgeNumber([Rect(0, 0, 10, 10)]));
        Assert.Equal(4, AnnotationOps.NextBadgeNumber([Badge(1), Badge(2), Badge(3)]));
        Assert.Equal(4, AnnotationOps.NextBadgeNumber([Badge(1), Badge(3)]));
        Assert.Equal(6, AnnotationOps.NextBadgeNumber([Badge(5)]));
        Assert.Equal(1000, AnnotationOps.NextBadgeNumber([Badge(999), Rect(0, 0, 10, 10)]));
    }

    [Fact]
    public void T02_deleting_a_badge_does_not_renumber_others()
    {
        var h = new EditHistory();
        var b1 = Badge(1, 10, 10);
        var b2 = Badge(2, 30, 10);
        var b3 = Badge(3, 50, 10);
        h.Execute(h.Current.Add(b1));
        h.Execute(h.Current.Add(b2));
        h.Execute(h.Current.Add(b3));
        h.Execute(h.Current.Remove(b2.Id));
        Assert.Equal([b1, b3], h.Current.ToList());
        Assert.Equal(4, AnnotationOps.NextBadgeNumber(h.Current));
        h.Execute(h.Current.Add(Badge(4)));
        h.Undo();
        Assert.DoesNotContain(h.Current, a => a is BadgeAnnotation { Number: 4 });
        Assert.Equal(4, AnnotationOps.NextBadgeNumber(h.Current));
    }

    [Fact]
    public void T03_badge_geometry()
    {
        Assert.Equal(32, BadgeGeometry.Diameter(20), 9);
        Assert.Equal(16, BadgeGeometry.Radius(20), 9);
        Assert.Equal(19.2, BadgeGeometry.Diameter(12), 9);
        var b = new BadgeAnnotation(Guid.NewGuid(), new PointD(100, 50), 1, Red, 20);
        Assert.Equal(new RectD(84, 34, 32, 32), AnnotationOps.BoundsOf(b, FixedMeasure));
    }

    [Fact]
    public void T04_badge_hit_test_is_a_circle()
    {
        var b = new BadgeAnnotation(Guid.NewGuid(), new PointD(100, 100), 1, Red, 20);
        Assert.True(AnnotationOps.HitTest(b, new PointD(118, 100), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(b, new PointD(118.5, 100), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(b, new PointD(112, 112), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(b, new PointD(114, 114), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(b, new PointD(100, 100), 2, FixedMeasure));
    }

    [Fact]
    public void T05_badge_has_no_handles_and_moves_as_a_whole()
    {
        var b = new BadgeAnnotation(Guid.NewGuid(), new PointD(100, 100), 7, Red, 20);
        PointD[] points = [new(84, 84), new(116, 84), new(84, 116), new(116, 116), new(100, 84), new(116, 100), new(100, 116), new(84, 100)];
        foreach (var p in points) Assert.Equal(HandleKind.None, AnnotationOps.HitHandle(b, p, 5));
        var moved = (BadgeAnnotation)AnnotationOps.ApplyHandleDrag(b, HandleKind.Body, 10, -5);
        Assert.Equal(new PointD(110, 95), moved.Center);
        Assert.Equal(b with { Center = new PointD(110, 95) }, moved);
        Assert.Equal(moved, b.MoveBy(10, -5));
        Assert.False(AnnotationOps.IsDegenerate(b, 4));
        Assert.False(AnnotationOps.IsDegenerate(b, 40));
    }

    [Fact]
    public void T06_badge_style()
    {
        Assert.Equal(new StyleSupport(true, false, false, true, false), StyleSupport.For(AnnotationKind.Badge));
        var b = Badge(1);
        var black = (BadgeAnnotation)AnnotationOps.WithStyle(b, color: Black);
        Assert.Equal(Black, black.Color);
        Assert.Equal(b.Id, black.Id);
        var big = (BadgeAnnotation)AnnotationOps.WithStyle(b, fontSize: 32);
        Assert.Equal(32, big.FontSize);
        Assert.Same(b, AnnotationOps.WithStyle(b, strokeWidth: 8, fontFamily: "Arial"));
        Assert.Same(b, AnnotationOps.WithStyle(b, outline: true));
    }

    [Fact]
    public void T07_badge_is_drawn_above_mosaic_in_creation_order()
    {
        var a = Rect(0, 0, 100, 100);
        var m = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 100));
        var b = Badge(1, 50, 50);
        var c = Rect(40, 40, 20, 20);
        Annotation[] list = [a, m, b, c];
        Assert.Equal([m, a, b, c], AnnotationOps.InRenderOrder(list).ToList());
        Assert.Same(c, AnnotationOps.HitTop(list, new PointD(50, 50), 2, FixedMeasure));
        Assert.Same(b, AnnotationOps.HitTop([a, m, b], new PointD(50, 50), 2, FixedMeasure));
        Assert.Same(a, AnnotationOps.HitTop([a, m], new PointD(50, 50), 2, FixedMeasure));
    }

    // ---- 矢印キーの移動（AC-2） ----

    [Fact]
    public void T11_body_move_works_for_every_kind()
    {
        Annotation[] all =
        [
            Rect(0, 0, 10, 10),
            new ArrowAnnotation(Guid.NewGuid(), new PointD(1, 2), new PointD(30, 40), Red, 3),
            new LineAnnotation(Guid.NewGuid(), new PointD(1, 2), new PointD(30, 40), Red, 3),
            new TextAnnotation(Guid.NewGuid(), new PointD(5, 6), "a", Red, "Meiryo", 20),
            new MosaicAnnotation(Guid.NewGuid(), new RectD(5, 6, 20, 20)),
            Badge(1, 50, 50),
            new HighlightAnnotation(Guid.NewGuid(), new RectD(5, 6, 20, 20), Red),
            new CropAnnotation(Guid.NewGuid(), new RectD(5, 6, 20, 20)),
        ];
        foreach (var a in all)
        {
            foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 10.0), (0.0, -10.0) })
            {
                var moved = AnnotationOps.ApplyHandleDrag(a, HandleKind.Body, dx, dy);
                Assert.Equal(a.MoveBy(dx, dy), moved);
                Assert.Equal(a.Id, moved.Id);
                var before = AnnotationOps.BoundsOf(a, FixedMeasure);
                var after = AnnotationOps.BoundsOf(moved, FixedMeasure);
                Assert.Equal(before.Offset(dx, dy), after);
            }
        }
        var atOrigin = (RectangleAnnotation)AnnotationOps.ApplyHandleDrag(Rect(0, 0, 10, 10), HandleKind.Body, -1, 0);
        Assert.Equal(-1, atOrigin.Bounds.X);
    }

    // ---- ハイライト（AC-4） ----

    [Fact]
    public void T25_highlight_behaves_like_a_rectangle()
    {
        var bounds = new RectD(10, 10, 40, 20);
        var h = new HighlightAnnotation(Guid.NewGuid(), bounds, Red);
        var r = new RectangleAnnotation(Guid.NewGuid(), bounds, Red, 0);
        Assert.Equal(bounds, AnnotationOps.BoundsOf(h, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(h, new PointD(30, 20), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(h, new PointD(52, 20), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(h, new PointD(52.5, 20), 2, FixedMeasure));
        foreach (var kind in HandleMath.RectHandles)
        {
            var p = HandleMath.PositionOf(bounds, kind);
            Assert.Equal(AnnotationOps.HitHandle(r, p, 3), AnnotationOps.HitHandle(h, p, 3));
            Assert.Equal(kind, AnnotationOps.HitHandle(h, p, 3));
        }
        var flipped = (HighlightAnnotation)AnnotationOps.ApplyHandleDrag(h, HandleKind.Right, -50, 0);
        Assert.True(flipped.Bounds.Width >= 0 && flipped.Bounds.Height >= 0);
        Assert.Equal(((RectangleAnnotation)AnnotationOps.ApplyHandleDrag(r, HandleKind.Right, -50, 0)).Bounds, flipped.Bounds);
        var shaped = (HighlightAnnotation)AnnotationOps.ShapeFromDrag(h, new PointD(50, 30), new PointD(10, 10));
        Assert.Equal(new RectD(10, 10, 40, 20), shaped.Bounds);
        foreach (var size in new[] { 1.0, 20.0, 30.0 })
            Assert.Equal(AnnotationOps.IsDegenerate(r, size), AnnotationOps.IsDegenerate(h, size));
        Assert.Equal(new StyleSupport(true, false, false, false), StyleSupport.For(AnnotationKind.Highlight));
        Assert.Same(h, AnnotationOps.WithStyle(h, strokeWidth: 8));
        Assert.Equal(Black, ((HighlightAnnotation)AnnotationOps.WithStyle(h, color: Black)).Color);
        Assert.Equal(0x60, h.FillColor.A);
    }

    // ---- 文字の縁取り（AC-5） ----

    [Fact]
    public void T29_outline_color_and_thickness()
    {
        Assert.Equal(Black, TextOutlineStyle.ColorFor(White));
        Assert.Equal(White, TextOutlineStyle.ColorFor(Black));
        Assert.Equal(Black, TextOutlineStyle.ColorFor(ArgbColor.FromRgb(128, 128, 128)));
        Assert.Equal(White, TextOutlineStyle.ColorFor(ArgbColor.FromRgb(127, 127, 127)));
        Assert.Equal(White, TextOutlineStyle.ColorFor(Red));
        Assert.Equal(Black, TextOutlineStyle.ColorFor(ArgbColor.FromRgb(255, 255, 0)));
        Assert.Equal(1, TextOutlineStyle.Thickness(5), 9);
        Assert.Equal(1, TextOutlineStyle.Thickness(10), 9);
        Assert.Equal(1.2, TextOutlineStyle.Thickness(12), 9);
        Assert.Equal(2, TextOutlineStyle.Thickness(20), 9);
        Assert.Equal(3, TextOutlineStyle.Thickness(30), 9);
        Assert.Equal(3, TextOutlineStyle.Thickness(31), 9);
        Assert.Equal(3, TextOutlineStyle.Thickness(48), 9);
    }

    [Fact]
    public void T30_outline_flag_and_style_support()
    {
        var t = new TextAnnotation(Guid.NewGuid(), new PointD(10, 10), "abc", Red, "Meiryo", 20);
        Assert.False(t.Outline);
        Assert.Equal(new StyleSupport(true, false, true, true, true), StyleSupport.For(AnnotationKind.Text));
        foreach (var kind in new AnnotationKind?[] { AnnotationKind.Rectangle, AnnotationKind.Arrow, AnnotationKind.Line, AnnotationKind.Mosaic,
                     AnnotationKind.Badge, AnnotationKind.Highlight, AnnotationKind.Crop, null })
            Assert.False(StyleSupport.For(kind).Outline);
        var outlined = (TextAnnotation)AnnotationOps.WithStyle(t, outline: true);
        Assert.True(outlined.Outline);
        Assert.Equal(t with { Outline = true }, outlined);
        Assert.Same(t, AnnotationOps.WithStyle(t, outline: null));
        var r = Rect(0, 0, 10, 10);
        Assert.Same(r, AnnotationOps.WithStyle(r, outline: true));
        Assert.Equal(AnnotationOps.BoundsOf(t, FixedMeasure), AnnotationOps.BoundsOf(outlined, FixedMeasure));
        foreach (var p in new[] { new PointD(52, 20), new PointD(52.5, 20) })
            Assert.Equal(AnnotationOps.HitTest(t, p, 2, FixedMeasure), AnnotationOps.HitTest(outlined, p, 2, FixedMeasure));
    }

    // ---- トリミング（AC-6） ----

    [Fact]
    public void T35_crop_pixel_rect_rounding_and_output_size()
    {
        PixelRect Px(double x, double y, double w, double h) => CropMath.ToPixelRect(new RectD(x, y, w, h), 100, 100);
        Assert.Equal(new PixelRect(10, 20, 30, 40), Px(10, 20, 30, 40));
        Assert.Equal(new PixelRect(10, 10, 20, 20), Px(10.5, 10.5, 20, 20));
        Assert.Equal(new PixelRect(12, 12, 20, 20), Px(11.5, 11.5, 20, 20));
        Assert.Equal(new PixelRect(0, 0, 40, 40), Px(-10, -10, 50, 50));
        Assert.Equal(new PixelRect(90, 90, 10, 10), Px(90, 90, 20, 20));
        Assert.True(Px(150, 150, 10, 10).IsEmpty);
        Assert.True(Px(10, 10, 0, 20).IsEmpty);
        Assert.True(Px(10.6, 10, 0.3, 20).IsEmpty);
        Assert.Equal(new PixelRect(10, 10, 1, 20), Px(10.2, 10, 0.4, 20));
        Assert.Equal(new PixelRect(0, 0, 100, 100), Px(0, 0, 100, 100));
        Assert.Equal(new PixelRect(0, 0, 1, 1), Px(0, 0, 1, 1));

        Assert.Equal((100, 100), CropMath.OutputSize(100, 100, [Rect(0, 0, 10, 10)]));
        Assert.Equal((30, 40), CropMath.OutputSize(100, 100, [new CropAnnotation(Guid.NewGuid(), new RectD(10, 20, 30, 40))]));
        Assert.Equal((100, 100), CropMath.OutputSize(100, 100, [new CropAnnotation(Guid.NewGuid(), new RectD(150, 150, 10, 10))]));
    }

    [Fact]
    public void T36_only_one_crop_and_history_covers_it()
    {
        var r1 = Rect(0, 0, 10, 10);
        var r2 = Rect(20, 20, 10, 10);
        var a = new CropAnnotation(Guid.NewGuid(), new RectD(1, 1, 50, 50));
        var b = new CropAnnotation(Guid.NewGuid(), new RectD(5, 5, 20, 20));
        var replaced = ImmutableList.Create<Annotation>(r1, a, r2).ReplaceCrop(b);
        Assert.Equal([r1, r2, b], replaced.ToList());
        Assert.Same(r1, replaced[0]);
        Assert.Same(r2, replaced[1]);
        Assert.Equal([r1, b], ImmutableList.Create<Annotation>(r1).ReplaceCrop(b).ToList());
        Assert.Null(AnnotationOps.CropOf([r1]));
        Assert.Same(a, AnnotationOps.CropOf([a, b]));

        var h = new EditHistory();
        h.Execute(ImmutableList.Create<Annotation>(r1, a));
        h.Execute(h.Current.ReplaceCrop(b));
        h.Undo();
        Assert.Equal([r1, a], h.Current.ToList());
        h.Redo();
        Assert.Equal([r1, b], h.Current.ToList());
        h.Execute([]);
        Assert.Empty(h.Current);
        h.Undo();
        Assert.Equal([r1, b], h.Current.ToList());
    }

    [Fact]
    public void T37_crop_is_hit_only_near_its_frame_and_last()
    {
        var c = new CropAnnotation(Guid.NewGuid(), new RectD(20, 20, 60, 60));
        Assert.False(AnnotationOps.HitTest(c, new PointD(50, 50), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(c, new PointD(20, 50), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(c, new PointD(18, 50), 2, FixedMeasure));
        Assert.True(AnnotationOps.HitTest(c, new PointD(22, 50), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(c, new PointD(17.5, 50), 2, FixedMeasure));
        Assert.False(AnnotationOps.HitTest(c, new PointD(22.5, 50), 2, FixedMeasure));

        var inner = Rect(40, 40, 20, 20);
        Assert.Same(inner, AnnotationOps.HitTop([c, inner], new PointD(40, 50), 2, FixedMeasure));
        Assert.Same(c, AnnotationOps.HitTop([c], new PointD(20, 50), 2, FixedMeasure));
        var overlapping = Rect(20, 20, 30, 30);
        Assert.Same(overlapping, AnnotationOps.HitTop([c, overlapping], new PointD(20, 40), 2, FixedMeasure));

        var m = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 10, 10));
        var r = Rect(0, 0, 10, 10);
        Assert.Equal([m, r, c], AnnotationOps.InRenderOrder([c, m, r]).ToList());

        foreach (var kind in HandleMath.RectHandles)
            Assert.Equal(kind, AnnotationOps.HitHandle(c, HandleMath.PositionOf(c.Bounds, kind), 3));
        var flipped = (CropAnnotation)AnnotationOps.ApplyHandleDrag(c, HandleKind.Left, 80, 0);
        Assert.Equal(new RectD(80, 20, 20, 60), flipped.Bounds);
        Assert.Equal(StyleSupport.None, StyleSupport.For(AnnotationKind.Crop));
        Assert.Same(c, AnnotationOps.WithStyle(c, color: Black));
        var rc = new RectangleAnnotation(Guid.NewGuid(), c.Bounds, Red, 1);
        foreach (var size in new[] { 1.0, 60.0, 61.0 })
            Assert.Equal(AnnotationOps.IsDegenerate(rc, size), AnnotationOps.IsDegenerate(c, size));
    }
}
