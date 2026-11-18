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
    /// <summary>番号バッジ（V1.4.0）。</summary>
    Badge,
    /// <summary>ハイライト（半透明の塗り。V1.4.0）。</summary>
    Highlight,
    /// <summary>トリミングの範囲（一覧に最大 1 つ。V1.4.0）。</summary>
    Crop,
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

/// <summary>文字。<see cref="Position"/> は文字列の左上。改行を含んでよい。<see cref="Outline"/> は縁取り（V1.4.0）。</summary>
public sealed record TextAnnotation(Guid Id, PointD Position, string Text, ArgbColor Color, string FontFamily, double FontSize, bool Outline = false) : Annotation(Id)
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

/// <summary>番号バッジ（V1.4.0）。塗りつぶした円（直径 = 文字サイズ × 1.6）に白の太字で番号を描く。</summary>
public sealed record BadgeAnnotation(Guid Id, PointD Center, int Number, ArgbColor Color, double FontSize) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Badge;
    public override Annotation MoveBy(double dx, double dy) => this with { Center = Center.Offset(dx, dy) };
}

/// <summary>ハイライト（V1.4.0）。枠線なし、塗りの不透明度は固定（<see cref="Alpha"/>）。</summary>
public sealed record HighlightAnnotation(Guid Id, RectD Bounds, ArgbColor Color) : Annotation(Id)
{
    /// <summary>塗りの不透明度（色の A を置き換える）。</summary>
    public const byte Alpha = 0x60;

    public override AnnotationKind Kind => AnnotationKind.Highlight;
    public override Annotation MoveBy(double dx, double dy) => this with { Bounds = Bounds.Offset(dx, dy) };

    /// <summary>描画に使う色（A を <see cref="Alpha"/> にする）。</summary>
    public ArgbColor FillColor => Color with { A = Alpha };
}

/// <summary>
/// トリミングの範囲（V1.4.0）。一覧に最大 1 つ（<see cref="AnnotationOps.ReplaceCrop"/>）。
/// 座標は元画像のままで、他の注釈の座標は変えない。出力時に合成してから切り出す。
/// </summary>
public sealed record CropAnnotation(Guid Id, RectD Bounds) : Annotation(Id)
{
    public override AnnotationKind Kind => AnnotationKind.Crop;
    public override Annotation MoveBy(double dx, double dy) => this with { Bounds = Bounds.Offset(dx, dy) };
}
