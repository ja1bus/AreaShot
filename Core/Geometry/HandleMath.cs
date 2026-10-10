namespace AreaShot.Core.Geometry;

/// <summary>選択中オブジェクトの操作点（つまみ）。</summary>
public enum HandleKind
{
    None,
    /// <summary>本体（ドラッグで移動）。</summary>
    Body,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    ArrowStart,
    ArrowEnd,
}

/// <summary>つまみの位置と、つまみによる変形の計算（仕様書 §10.3）。</summary>
public static class HandleMath
{
    public static readonly IReadOnlyList<HandleKind> RectHandles =
    [
        HandleKind.TopLeft, HandleKind.Top, HandleKind.TopRight, HandleKind.Right,
        HandleKind.BottomRight, HandleKind.Bottom, HandleKind.BottomLeft, HandleKind.Left,
    ];

    /// <summary>矩形の 8 方向のつまみの位置（画像座標）。</summary>
    public static PointD PositionOf(RectD r, HandleKind handle) => handle switch
    {
        HandleKind.TopLeft => new(r.Left, r.Top),
        HandleKind.Top => new(r.Center.X, r.Top),
        HandleKind.TopRight => new(r.Right, r.Top),
        HandleKind.Right => new(r.Right, r.Center.Y),
        HandleKind.BottomRight => new(r.Right, r.Bottom),
        HandleKind.Bottom => new(r.Center.X, r.Bottom),
        HandleKind.BottomLeft => new(r.Left, r.Bottom),
        HandleKind.Left => new(r.Left, r.Center.Y),
        _ => r.Center,
    };

    /// <summary>
    /// ドラッグ開始時の矩形 <paramref name="original"/> を、つまみ <paramref name="handle"/> を (dx, dy) 動かした形へ変形する。
    /// 反対側の辺を越えた場合は裏返して正規化する（幅・高さは常に 0 以上）。
    /// </summary>
    public static RectD Resize(RectD original, HandleKind handle, double dx, double dy)
    {
        double l = original.Left, t = original.Top, r = original.Right, b = original.Bottom;
        switch (handle)
        {
            case HandleKind.TopLeft: l += dx; t += dy; break;
            case HandleKind.Top: t += dy; break;
            case HandleKind.TopRight: r += dx; t += dy; break;
            case HandleKind.Right: r += dx; break;
            case HandleKind.BottomRight: r += dx; b += dy; break;
            case HandleKind.Bottom: b += dy; break;
            case HandleKind.BottomLeft: l += dx; b += dy; break;
            case HandleKind.Left: l += dx; break;
            case HandleKind.Body: return original.Offset(dx, dy);
            default: return original;
        }
        return RectD.FromEdges(l, t, r, b);
    }
}
