using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;

namespace AreaShot.Imaging;

/// <summary>
/// 文字の組版。画面表示・当たり判定・出力で同じ FormattedText を使い、位置と大きさを一致させる。
/// 文字サイズは元画像のピクセル単位（20 = 高さ約 20px の文字）。
/// pixelsPerDip は 1 固定（出力は 96 DPI = 1 ピクセル単位で描くため）。
/// </summary>
public static class TextLayout
{
    public static FormattedText Create(TextAnnotation text)
    {
        var typeface = new Typeface(FontCatalog.GetFamily(text.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(
            text.Text.Length == 0 ? " " : text.Text,
            CultureInfo.GetCultureInfo("ja-JP"),
            FlowDirection.LeftToRight,
            typeface,
            Math.Max(1, text.FontSize),
            Brushes.Black,
            pixelsPerDip: 1.0);
        ft.SetForegroundBrush(BrushCache.Get(text.Color));
        return ft;
    }

    public static SizeD Measure(TextAnnotation text)
    {
        var ft = Create(text);
        // WidthIncludingTrailingWhitespace: 末尾の空白も選択範囲に含める。
        return new SizeD(Math.Max(ft.WidthIncludingTrailingWhitespace, 1), Math.Max(ft.Height, 1));
    }

    /// <summary>Core の当たり判定へ渡す測定関数。</summary>
    public static readonly TextMeasurer Measurer = Measure;
}

/// <summary>色 → 凍結済みブラシ。</summary>
public static class BrushCache
{
    private static readonly Dictionary<ArgbColor, SolidColorBrush> Brushes = [];
    private static readonly object Gate = new();

    public static SolidColorBrush Get(ArgbColor c)
    {
        lock (Gate)
        {
            if (!Brushes.TryGetValue(c, out var brush))
            {
                brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
                brush.Freeze();
                Brushes[c] = brush;
            }
            return brush;
        }
    }

    public static Color ToColor(ArgbColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
}
