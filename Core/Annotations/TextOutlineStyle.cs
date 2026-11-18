namespace AreaShot.Core.Annotations;

/// <summary>文字の縁取りの色と太さ（V1.4.0。仕様書 §10）。</summary>
public static class TextOutlineStyle
{
    private static readonly ArgbColor Black = ArgbColor.FromRgb(0, 0, 0);
    private static readonly ArgbColor White = ArgbColor.FromRgb(255, 255, 255);

    /// <summary>文字色の輝度（0.299R + 0.587G + 0.114B）が 128 以上なら黒、未満なら白。</summary>
    /// <remarks>浮動小数点の誤差で灰色 (128,128,128) が 128 未満にならないよう、1000 倍の整数で比べる。</remarks>
    public static ArgbColor ColorFor(ArgbColor textColor) =>
        LuminanceTimes1000(textColor) >= 128_000 ? Black : White;

    public static int LuminanceTimes1000(ArgbColor c) => 299 * c.R + 587 * c.G + 114 * c.B;

    /// <summary>縁の太さ（文字の外側へ出る幅。画像ピクセル）。文字サイズ / 10 を 1～3 に収める。</summary>
    public static double Thickness(double fontSize) =>
        double.IsFinite(fontSize) ? Math.Clamp(fontSize / 10, 1, 3) : 1;
}
