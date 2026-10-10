namespace AreaShot.Core.Fonts;

/// <summary>
/// 文字のフォントの決定（仕様書 §10.5）。指定フォントが無い場合は
/// Meiryo → Yu Gothic UI → システム既定フォント の順に代替する。例外にはしない。
/// </summary>
public static class FontFallback
{
    public static readonly IReadOnlyList<string> PreferredFallbacks = ["Meiryo", "Yu Gothic UI"];

    /// <summary>
    /// 選択肢に出す候補（インストールされているものだけを表示する）。
    /// 仕様の最低限（Meiryo / Yu Gothic UI / MS Gothic）に、Windows 11 に標準で入る日本語・欧文フォントを加えている。
    /// </summary>
    public static readonly IReadOnlyList<string> Candidates =
    [
        "Meiryo", "Meiryo UI", "Yu Gothic UI", "Yu Gothic", "MS Gothic", "MS UI Gothic", "MS PGothic",
        "BIZ UDGothic", "BIZ UDPGothic", "Yu Mincho", "MS Mincho", "Segoe UI", "Arial",
    ];

    public static string Resolve(string? requested, Func<string, bool> isInstalled, string systemDefault)
    {
        if (!string.IsNullOrWhiteSpace(requested) && SafeIsInstalled(isInstalled, requested)) return requested;
        foreach (var name in PreferredFallbacks)
            if (SafeIsInstalled(isInstalled, name)) return name;
        return string.IsNullOrWhiteSpace(systemDefault) ? "Segoe UI" : systemDefault;
    }

    private static bool SafeIsInstalled(Func<string, bool> isInstalled, string name)
    {
        try { return isInstalled(name); }
        catch (Exception) { return false; } // フォント列挙の失敗でアプリを止めない
    }
}
