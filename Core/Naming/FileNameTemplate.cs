using System.Text;
using System.Text.RegularExpressions;
using AreaShot.Core.Localization;

namespace AreaShot.Core.Naming;

/// <summary>日時変数。説明（設定画面に表示する）は表示言語の文字列を参照する。</summary>
public sealed record TemplateToken(string Token, string DescriptionKey, string Format)
{
    public string Description => Strings.ResourceManager.GetString(DescriptionKey, Strings.Culture) ?? DescriptionKey;
}

/// <summary>
/// 保存ファイル名のテンプレート（仕様書 §12.2）。例: "{yy}{MM}{dd}{HH}{mm}{ss}_capture" → "260922224012_capture"。
/// 拡張子は含めない（画像形式から付ける）。ミリ秒の変数は用意しない。
/// </summary>
public static partial class FileNameTemplate
{
    public const string Default = "{yy}{MM}{dd}{HH}{mm}{ss}_capture";

    public static readonly IReadOnlyList<TemplateToken> Tokens =
    [
        new("{yyyy}", nameof(Strings.TokenYear4), "yyyy"),
        new("{yy}", nameof(Strings.TokenYear2), "yy"),
        new("{MM}", nameof(Strings.TokenMonth), "MM"),
        new("{dd}", nameof(Strings.TokenDay), "dd"),
        new("{HH}", nameof(Strings.TokenHour), "HH"),
        new("{mm}", nameof(Strings.TokenMinute), "mm"),
        new("{ss}", nameof(Strings.TokenSecond), "ss"),
    ];

    /// <summary>Windows の予約デバイス名。拡張子が付いても（"CON.capture" など）予約名として扱われる。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// テンプレートの誤りを返す。問題が無ければ null。
    /// 未知の変数・ファイル名に使えない文字・空の結果を誤りとする。
    /// </summary>
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return Strings.TemplateEmpty;
        foreach (Match m in TokenPattern().Matches(template))
        {
            if (!Tokens.Any(t => t.Token == m.Value)) return string.Format(Strings.TemplateUnknownTokenFormat, m.Value);
        }
        var literal = TokenPattern().Replace(template, "");
        var invalid = literal.Where(c => Path.GetInvalidFileNameChars().Contains(c)).Distinct().ToArray();
        if (invalid.Length > 0) return string.Format(Strings.TemplateInvalidCharsFormat, string.Join(" ", invalid.Select(Describe)));
        return null;
    }

    /// <summary>
    /// テンプレートを日時で展開する。誤りがあっても例外にせず、使えない文字は "_" に置き換える。
    /// 結果が空になる場合は既定のテンプレートを使う。
    /// </summary>
    public static string Format(string? template, DateTime time)
    {
        if (string.IsNullOrWhiteSpace(template)) template = Default;
        var expanded = TokenPattern().Replace(template, m =>
        {
            var token = Tokens.FirstOrDefault(t => t.Token == m.Value);
            return token is null ? m.Value : time.ToString(token.Format, System.Globalization.CultureInfo.InvariantCulture);
        });
        var name = Sanitize(expanded);
        return name.Length == 0 ? (template == Default ? "capture" : Format(Default, time)) : name;
    }

    /// <summary>
    /// ファイル名に使えない文字を "_" にし、末尾の空白・ピリオドを除く。
    /// 最初のピリオドより前（その直前の空白は除く）が予約名なら、その後ろに "_" を付ける（"CON.capture" → "CON_.capture"。V1.2.0）。
    /// </summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        var result = sb.ToString().Trim().TrimEnd('.', ' ');
        var dot = result.IndexOf('.');
        var stem = (dot < 0 ? result : result[..dot]).TrimEnd(' ');
        if (ReservedNames.Contains(stem)) result = result.Insert(stem.Length, "_");
        return result;
    }

    private static string Describe(char c) => c switch
    {
        '\t' => Strings.CharTab,
        < ' ' => string.Format(Strings.CharControlFormat, ((int)c).ToString("X2")),
        _ => c.ToString(),
    };
}
