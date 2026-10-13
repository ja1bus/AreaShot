using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AreaShot.Views;

/// <summary>
/// フォルダのパスを、幅に収まらなければ途中を「…」にして表示する（V1.3.0）。"C:\…\手順書\images" のように末尾のフォルダを残す。
/// フルパスはツールチップで見せる。幅は親のレイアウトで決まる前提（横に伸ばして置く）。
/// </summary>
public sealed class CompactPathText : TextBlock
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(
        nameof(Path), typeof(string), typeof(CompactPathText),
        new PropertyMetadata("", (d, _) => ((CompactPathText)d).Refresh()));

    /// <summary>パスの後ろに付ける文字（「（見つかりません）」など）。省略しない。</summary>
    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(CompactPathText),
        new PropertyMetadata("", (d, _) => ((CompactPathText)d).Refresh()));

    public CompactPathText()
    {
        TextTrimming = TextTrimming.CharacterEllipsis; // 最後のフォルダ名だけでも入らない場合
        SizeChanged += (_, _) => Refresh();
    }

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    private void Refresh()
    {
        var path = Path ?? "";
        ToolTip = path.Length == 0 ? null : path;
        var width = ActualWidth;
        Text = width <= 0 ? path + Suffix : Compact(path, width) + Suffix;
    }

    private string Compact(string path, double width)
    {
        var available = width - Measure(Suffix);
        if (Measure(path) <= available) return path;
        // 先頭（ドライブ・サーバー名）と末尾のフォルダを残し、間を「…」にする。残す末尾を 1 つずつ減らす。
        var root = System.IO.Path.GetPathRoot(path) ?? "";
        var parts = path[root.Length..].Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var keep = parts.Length - 1; keep >= 1; keep--)
        {
            var candidate = root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar + "…"
                            + System.IO.Path.DirectorySeparatorChar + string.Join(System.IO.Path.DirectorySeparatorChar, parts[^keep..]);
            if (Measure(candidate) <= available) return candidate;
        }
        return "…" + System.IO.Path.DirectorySeparatorChar + (parts.Length > 0 ? parts[^1] : path);
    }

    private double Measure(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace + Padding.Left + Padding.Right;
    }
}
