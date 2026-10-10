namespace AreaShot.Core.Geometry;

/// <summary>画像座標（元画像のピクセル単位、実数）の点。</summary>
public readonly record struct PointD(double X, double Y)
{
    public PointD Offset(double dx, double dy) => new(X + dx, Y + dy);

    public static double Distance(PointD a, PointD b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>画像座標の大きさ。</summary>
public readonly record struct SizeD(double Width, double Height);

/// <summary>画像座標の矩形。Width / Height は 0 以上を前提とする（<see cref="FromPoints"/> で正規化する）。</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public PointD Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>2 点を対角とする矩形（向きは問わない）。</summary>
    public static RectD FromPoints(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    public static RectD FromEdges(double left, double top, double right, double bottom) =>
        FromPoints(new PointD(left, top), new PointD(right, bottom));

    public RectD Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    public RectD Inflate(double amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public bool Contains(PointD p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
}

/// <summary>整数ピクセルの矩形（右端・下端は含まない）。画面座標・画像のピクセル範囲に使う。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public PixelRect Intersect(PixelRect other)
    {
        var l = Math.Max(Left, other.Left);
        var t = Math.Max(Top, other.Top);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? default : FromEdges(l, t, r, b);
    }

    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}
