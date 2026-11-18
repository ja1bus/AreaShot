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

    /// <summary>測定結果のキャッシュの上限（超えたら全消去する）。</summary>
    public const int CacheLimit = 512;

    /// <summary>測定に効く値だけのキー（位置・色・Id・縁取りは大きさに影響しない）。</summary>
    private readonly record struct MeasureKey(string Text, string FontFamily, double FontSize);

    private static readonly Dictionary<MeasureKey, SizeD> MeasureCache = [];
    private static readonly object CacheGate = new();

    /// <summary>今のキャッシュの件数（試験用）。</summary>
    public static int CachedCount
    {
        get { lock (CacheGate) return MeasureCache.Count; }
    }

    /// <summary>
    /// 文字の大きさ（画像座標）。マウスの移動のたびに全文字を組版しないよう、内容・フォント・文字サイズごとに結果を覚える（V1.4.0）。
    /// </summary>
    public static SizeD Measure(TextAnnotation text)
    {
        var key = new MeasureKey(text.Text, text.FontFamily ?? "", text.FontSize);
        lock (CacheGate)
        {
            if (MeasureCache.TryGetValue(key, out var cached)) return cached;
        }
        var size = MeasureUncached(text);
        lock (CacheGate)
        {
            if (MeasureCache.Count >= CacheLimit) MeasureCache.Clear();
            MeasureCache[key] = size;
        }
        return size;
    }

    /// <summary>キャッシュを通さずに測る。</summary>
    public static SizeD MeasureUncached(TextAnnotation text)
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
