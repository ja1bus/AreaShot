using System.Collections.Immutable;
using AreaShot.Core.Annotations;
using AreaShot.Core.Editing;
using AreaShot.Core.Geometry;

namespace AreaShot.Tests;

public class EditHistoryTests
{
    private static readonly ArgbColor Red = EditorPresets.DefaultColor;
    private static readonly ArgbColor Black = EditorPresets.Colors[1].Color;

    private static RectangleAnnotation Rect(double x = 0) => new(Guid.NewGuid(), new RectD(x, 0, 10, 10), Red, 3);

    [Fact]
    public void Add_undo_redo()
    {
        var h = new EditHistory();
        var r = Rect();
        h.Execute(h.Current.Add(r));
        Assert.Single(h.Current);
        Assert.True(h.CanUndo);
        Assert.False(h.CanRedo);

        Assert.True(h.Undo());
        Assert.Empty(h.Current);
        Assert.True(h.CanRedo);

        Assert.True(h.Redo());
        Assert.Same(r, Assert.Single(h.Current));
    }

    [Fact]
    public void New_operation_clears_redo()
    {
        var h = new EditHistory();
        h.Execute(h.Current.Add(Rect()));
        h.Undo();
        h.Execute(h.Current.Add(Rect(20)));
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void Drag_preview_is_recorded_as_single_step()
    {
        var h = new EditHistory();
        var r = Rect();
        h.Execute(h.Current.Add(r));
        var before = h.UndoCount;

        h.BeginPreview();
        var baseList = h.Current;
        for (var i = 1; i <= 50; i++) h.Preview(baseList.Replace(r.MoveBy(i, i)));
        Assert.Equal(before, h.UndoCount); // ドラッグ中は積まない
        h.CommitPreview();

        Assert.Equal(before + 1, h.UndoCount);
        Assert.Equal(new RectD(50, 50, 10, 10), ((RectangleAnnotation)h.Current[0]).Bounds);
        h.Undo();
        Assert.Equal(new RectD(0, 0, 10, 10), ((RectangleAnnotation)h.Current[0]).Bounds);
    }

    [Fact]
    public void Preview_without_change_is_not_recorded()
    {
        var h = new EditHistory();
        h.Execute(h.Current.Add(Rect()));
        var count = h.UndoCount;
        h.BeginPreview();
        h.Preview(h.Current);
        h.CommitPreview();
        Assert.Equal(count, h.UndoCount);
    }

    [Fact]
    public void Cancel_preview_restores_state()
    {
        var h = new EditHistory();
        var r = Rect();
        h.Execute(h.Current.Add(r));
        h.BeginPreview();
        h.Preview(h.Current.Replace(r.MoveBy(100, 0)));
        h.CancelPreview();
        Assert.Same(r, h.Current[0]);
        Assert.Equal(1, h.UndoCount);
    }

    [Fact]
    public void Undo_is_disabled_during_preview()
    {
        var h = new EditHistory();
        h.Execute(h.Current.Add(Rect()));
        h.BeginPreview();
        Assert.False(h.CanUndo);
        Assert.False(h.Undo());
        h.CommitPreview();
        Assert.True(h.CanUndo);
    }

    [Fact]
    public void Clear_all_is_undoable()
    {
        var h = new EditHistory();
        h.Execute(h.Current.Add(Rect()).Add(Rect(20)).Add(new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 5, 5))));
        h.Execute([]);
        Assert.Empty(h.Current);
        h.Undo();
        Assert.Equal(3, h.Current.Count);
    }

    [Fact]
    public void Clear_all_on_empty_does_nothing()
    {
        var h = new EditHistory();
        h.Execute([]);
        Assert.False(h.CanUndo);
    }

    [Fact]
    public void Delete_and_style_changes_are_undoable()
    {
        var h = new EditHistory();
        var r = Rect();
        h.Execute(h.Current.Add(r));
        h.Execute(h.Current.Replace(AnnotationOps.WithStyle(r, color: Black, strokeWidth: 8)));
        var styled = (RectangleAnnotation)h.Current[0];
        Assert.Equal((Black, 8.0), (styled.Color, styled.StrokeWidth));

        h.Execute(h.Current.Remove(r.Id));
        Assert.Empty(h.Current);

        h.Undo();
        Assert.Equal(Black, ((RectangleAnnotation)h.Current[0]).Color);
        h.Undo();
        Assert.Equal(Red, ((RectangleAnnotation)h.Current[0]).Color);
    }

    [Fact]
    public void Text_content_and_font_changes_are_undoable()
    {
        var h = new EditHistory();
        var t = new TextAnnotation(Guid.NewGuid(), new PointD(1, 1), "before", Red, "Meiryo", 20);
        h.Execute(h.Current.Add(t));
        h.Execute(h.Current.Replace(t with { Text = "after" }));
        h.Execute(h.Current.Replace(((TextAnnotation)h.Current[0]) with { FontFamily = "MS Gothic", FontSize = 32 }));
        h.Undo();
        Assert.Equal(("after", "Meiryo", 20.0), Describe(h.Current[0]));
        h.Undo();
        Assert.Equal(("before", "Meiryo", 20.0), Describe(h.Current[0]));
        h.Redo();
        h.Redo();
        Assert.Equal(("after", "MS Gothic", 32.0), Describe(h.Current[0]));

        static (string, string, double) Describe(Annotation a) =>
            a is TextAnnotation x ? (x.Text, x.FontFamily, x.FontSize) : default;
    }

    [Fact]
    public void Identical_execute_is_ignored()
    {
        var h = new EditHistory();
        var list = ImmutableList.Create<Annotation>(Rect());
        h.Execute(list);
        h.Execute(list);
        Assert.Equal(1, h.UndoCount);
    }

    [Fact]
    public void History_limit_drops_oldest()
    {
        var h = new EditHistory(limit: 5);
        for (var i = 0; i < 10; i++) h.Execute(h.Current.Add(Rect(i)));
        Assert.Equal(5, h.UndoCount);
        while (h.Undo()) { }
        Assert.Equal(5, h.Current.Count); // 最古の 5 件は戻せない
    }

    [Fact]
    public void Changed_event_fires()
    {
        var h = new EditHistory();
        var count = 0;
        h.Changed += (_, _) => count++;
        h.Execute(h.Current.Add(Rect()));
        h.Undo();
        h.Redo();
        Assert.Equal(3, count);
    }
}
