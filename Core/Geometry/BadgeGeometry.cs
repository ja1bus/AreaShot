namespace AreaShot.Core.Geometry;

/// <summary>番号バッジの大きさ（V1.4.0。仕様書 §10）。円の直径は文字サイズ × 1.6。</summary>
public static class BadgeGeometry
{
    public const double DiameterPerFontSize = 1.6;

    public static double Diameter(double fontSize) => fontSize * DiameterPerFontSize;

    public static double Radius(double fontSize) => Diameter(fontSize) / 2;

    /// <summary>円の外接正方形（画像座標）。</summary>
    public static RectD Bounds(PointD center, double fontSize)
    {
        var r = Radius(fontSize);
        return new RectD(center.X - r, center.Y - r, r * 2, r * 2);
    }
}
