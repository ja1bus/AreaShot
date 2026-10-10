namespace AreaShot.Core.Geometry;

/// <summary>
/// キャプチャー範囲の計算（仕様書 §5）。座標はすべて物理ピクセルの仮想デスクトップ座標（負値あり）。
/// </summary>
public static class SelectionMath
{
    /// <summary>
    /// ドラッグの開始点と現在点から選択範囲を求める。どの方向へドラッグしてもよい。
    /// 点は仮想デスクトップ <paramref name="bounds"/> の範囲へ切り詰める。
    /// 幅は |x1 - x0|（押した画素から離した画素の手前まで）。表示する「幅 × 高さ」と保存画像の大きさは一致する。
    /// </summary>
    public static PixelRect FromDrag(int x0, int y0, int x1, int y1, PixelRect bounds)
    {
        x0 = Math.Clamp(x0, bounds.Left, bounds.Right);
        x1 = Math.Clamp(x1, bounds.Left, bounds.Right);
        y0 = Math.Clamp(y0, bounds.Top, bounds.Bottom);
        y1 = Math.Clamp(y1, bounds.Top, bounds.Bottom);
        return PixelRect.FromEdges(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>仮想デスクトップ座標の範囲を、仮想デスクトップ画像内の座標（左上 = 0,0）へ変換する。</summary>
    public static PixelRect ToImageSpace(PixelRect screenRect, PixelRect virtualBounds) =>
        screenRect.Offset(-virtualBounds.Left, -virtualBounds.Top);
}
