using System.Windows;
using System.Windows.Media;
using AreaShot.Core.Fonts;

namespace AreaShot.Imaging;

/// <summary>
/// インストール済みフォントの確認と代替（仕様書 §10.5）。
/// フォント一覧の取得は初回だけ行い、結果を保持する（<see cref="WarmUp"/> でバックグラウンドから先に済ませられる）。
/// </summary>
public static class FontCatalog
{
    private static readonly Lazy<HashSet<string>> Installed = new(LoadInstalled, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Task> BackgroundWarmUp = new(() => Task.Run(WarmUp), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    /// <summary>フォント一覧を先に読み込む（どのスレッドからでも可）。</summary>
    public static void WarmUp() => _ = Installed.Value;

    /// <summary>範囲選択中の先読みは初回だけ開始する。</summary>
    public static void WarmUpOnceInBackground() => _ = BackgroundWarmUp.Value;

    public static bool IsInstalled(string name) => Installed.Value.Contains(name);

    /// <summary>選択肢に出すフォント（候補のうちインストール済みのもの）。</summary>
    public static IReadOnlyList<string> Available =>
        FontFallback.Candidates.Where(IsInstalled).DefaultIfEmpty(SystemDefault).Distinct().ToList();

    public static string SystemDefault
    {
        get
        {
            try { return SystemFonts.MessageFontFamily.Source; }
            catch (Exception) { return "Segoe UI"; }
        }
    }

    /// <summary>実際に使うフォント名（無ければ Meiryo → Yu Gothic UI → システム既定）。</summary>
    public static string Resolve(string? name) => FontFallback.Resolve(name, IsInstalled, SystemDefault);

    /// <summary>代替を適用した FontFamily（キャッシュ）。</summary>
    public static FontFamily GetFamily(string? name)
    {
        var resolved = Resolve(name);
        lock (Gate)
        {
            if (!Families.TryGetValue(resolved, out var family))
            {
                family = new FontFamily(resolved);
                Families[resolved] = family;
            }
            return family;
        }
    }

    private static HashSet<string> LoadInstalled()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var family in Fonts.SystemFontFamilies)
            {
                set.Add(family.Source);
                foreach (var name in family.FamilyNames.Values) set.Add(name);
            }
        }
        catch (Exception)
        {
            // 列挙に失敗しても空の一覧で続行し、代替フォントを使う。
        }
        return set;
    }
}
