using System.Globalization;
using AreaShot.Core.Localization;

namespace AreaShot.Core.Annotations;

/// <summary>色（WPF 非依存）。文字列表記は "#AARRGGBB"。</summary>
public readonly record struct ArgbColor(byte A, byte R, byte G, byte B)
{
    public static ArgbColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";

    /// <summary>"#RRGGBB" / "#AARRGGBB" を読む。</summary>
    public static bool TryParse(string? text, out ArgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new ArgbColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>名前付きのプリセット色。名前（画面に表示する）は表示言語の文字列を参照する。</summary>
public sealed record NamedColor(string NameKey, ArgbColor Color)
{
    public string Name => Strings.ResourceManager.GetString(NameKey, Strings.Culture) ?? NameKey;
}
