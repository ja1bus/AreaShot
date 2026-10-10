using AreaShot.Core.Geometry;

namespace AreaShot.Core.Annotations;

/// <summary>編集オブジェクトの種類。</summary>
public enum AnnotationKind
{
    Rectangle,
    Arrow,
    Line,
    Text,
    Mosaic,
}

/// <summary>
/// 編集オブジェクト（仕様書 §10）。すべて不変（immutable）で、座標は元画像のピクセル座標。
/// 変更は <c>with</c> 式で新しいインスタンスを作り、履歴（<see cref="Editing.EditHistory"/>）へ渡す。
/// <see cref="Id"/> は変更しても同じ（選択状態・モザイクのキャッシュの識別に使う）。
/// </summary>
public abstract record Annotation(Guid Id)
{
    public abstract AnnotationKind Kind { get; }

    /// <summary>平行移動した複製。</summary>
    public abstract Annotation MoveBy(double dx, double dy);
}

/// <summary>枠線だけの四角形。</summary>
public sealed record RectangleAnnotation(Guid Id, RectD Bounds, ArgbColor Color, double StrokeWidth) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Rectangle;
    public override Annotation MoveBy(double dx, double dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}

/// <summary>終点側にだけ矢印が付く直線。矢印の大きさは線幅に連動する（<see cref="Geometry.ArrowGeometry"/>）。</summary>
public sealed record ArrowAnnotation(Guid Id, PointD Start, PointD End, ArgbColor Color, double StrokeWidth) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Arrow;
    public override Annotation MoveBy(double dx, double dy) => this with { Start = Start.Offset(dx, dy), End = End.Offset(dx, dy) };
}

/// <summary>矢印の無い直線（V1.0.6）。両端は丸。つまみは矢印と同じ始点・終点。</summary>
public sealed record LineAnnotation(Guid Id, PointD Start, PointD End, ArgbColor Color, double StrokeWidth) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Line;
    public override Annotation MoveBy(double dx, double dy) => this with { Start = Start.Offset(dx, dy), End = End.Offset(dx, dy) };
}

/// <summary>文字。<see cref="Position"/> は文字列の左上。改行を含んでよい。</summary>
public sealed record TextAnnotation(Guid Id, PointD Position, string Text, ArgbColor Color, string FontFamily, double FontSize) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Text;
    public override Annotation MoveBy(double dx, double dy) => this with { Position = Position.Offset(dx, dy) };
}

/// <summary>矩形モザイク。元画像の <see cref="Bounds"/> の範囲から生成する（<see cref="Imaging.Mosaic"/>）。</summary>
public sealed record MosaicAnnotation(Guid Id, RectD Bounds) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Mosaic;
    public override Annotation MoveBy(double dx, double dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}
