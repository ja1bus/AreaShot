namespace AreaShot.Core.Geometry;

/// <summary>
/// 矢印付き直線の形（仕様書 §10.2）。矢印（塗りつぶし三角形）の大きさは線幅から決める。
/// 軸の線は矢印の根元の少し先で止め、太い線の端が矢印の先から見えないようにする。
/// </summary>
public readonly record struct ArrowShape(PointD LineStart, PointD LineEnd, PointD Tip, PointD WingLeft, PointD WingRight, bool HasHead);

public static class ArrowGeometry
{
    /// <summary>矢印の長さ（先端から根元まで）。</summary>
    public static double HeadLength(double strokeWidth) => Math.Max(10, strokeWidth * 4 + 4);

    /// <summary>矢印の根元の幅の半分。</summary>
    public static double HeadHalfWidth(double strokeWidth) => HeadLength(strokeWidth) * 0.5;

    public static ArrowShape Compute(PointD start, PointD end, double strokeWidth)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6) return new ArrowShape(start, end, end, end, end, false);

        var ux = dx / length;
        var uy = dy / length;
        // 線が短い場合は矢印を線の長さに合わせて縮める（矢印が始点より後ろへはみ出さないように）。
        var head = Math.Min(HeadLength(strokeWidth), length);
        var half = head * 0.5;
        var baseX = end.X - ux * head;
        var baseY = end.Y - uy * head;
        // 法線方向（左右の羽）。
        var nx = -uy;
        var ny = ux;
        var lineEnd = new PointD(end.X - ux * head * 0.8, end.Y - uy * head * 0.8);
        if (head >= length) lineEnd = start;
        return new ArrowShape(
            start, lineEnd, end,
            new PointD(baseX + nx * half, baseY + ny * half),
            new PointD(baseX - nx * half, baseY - ny * half),
            true);
    }

    /// <summary>点と線分の距離（当たり判定用）。</summary>
    public static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return PointD.Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        return PointD.Distance(p, new PointD(a.X + t * dx, a.Y + t * dy));
    }
}
