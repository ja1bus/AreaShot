using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using AreaShot.Core.Input;
using AreaShot.Core.Localization;
using AreaShot.Core.Naming;
using AreaShot.Core.Settings;

namespace AreaShot.Tests;

/// <summary>テストの間だけ表示言語（CurrentUICulture）を切り替える。</summary>
public sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    public UiCultureScope(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}

/// <summary>表示言語（英語・日本語）の文字列と、言語の決め方（仕様書 §14.4）。</summary>
public sealed partial class LocalizationTests
{
    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
                  ?? throw new InvalidOperationException($"{culture.Name} の文字列がありません（ja\\AreaShot.Core.resources.dll）");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static readonly Dictionary<string, string> English = Load(CultureInfo.InvariantCulture);
    private static readonly Dictionary<string, string> Japanese = Load(CultureInfo.GetCultureInfo("ja"));

    [GeneratedRegex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}(?!\})")]
    private static partial Regex Placeholder();

    private static int[] Placeholders(string s) =>
        Placeholder().Matches(s.Replace("{{", "").Replace("}}", "")).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToArray();

    [Fact]
    public void Japanese_has_exactly_the_same_keys_as_English()
    {
        Assert.NotEmpty(English);
        Assert.Empty(English.Keys.Except(Japanese.Keys)); // 訳し忘れ
        Assert.Empty(Japanese.Keys.Except(English.Keys)); // 英語側に無いキー
    }

    [Fact]
    public void No_string_is_empty()
    {
        Assert.Empty(English.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key));
        Assert.Empty(Japanese.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key));
    }

    [Fact]
    public void Format_placeholders_match_between_languages()
    {
        foreach (var (key, en) in English)
        {
            Assert.True(Placeholders(en).SequenceEqual(Placeholders(Japanese[key])), $"{key}: 埋め込み位置 {{n}} が英語と日本語で違う");
        }
    }

    [Fact]
    public void Format_strings_can_be_formatted()
    {
        foreach (var (key, en) in English.Where(p => p.Key.EndsWith("Format", StringComparison.Ordinal)))
        {
            var count = Placeholders(en).Length;
            Assert.True(count > 0, $"{key}: 名前が Format で終わるのに {{0}} が無い");
            var args = Enumerable.Range(0, count).Select(i => (object)$"<{i}>").ToArray();
            Assert.Contains("<0>", string.Format(CultureInfo.InvariantCulture, en, args));
            Assert.Contains("<0>", string.Format(CultureInfo.InvariantCulture, Japanese[key], args));
        }
    }

    [Fact]
    public void Japanese_text_is_not_left_in_English_strings()
    {
        var japaneseChars = new Regex(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]");
        Assert.Empty(English.Where(p => japaneseChars.IsMatch(p.Value)).Select(p => p.Key));
    }

    [Theory]
    [InlineData("", "ja-JP", "ja")]
    [InlineData("", "ja", "ja")]
    [InlineData("", "en-US", "en")]
    [InlineData("", "de-DE", "en")] // 英語・日本語以外は英語
    [InlineData("", "zh-CN", "en")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("ja", "en-US", "ja")]
    [InlineData("JA", "en-US", "ja")]
    [InlineData("fr", "ja-JP", "ja")] // 未知の値は「Windows に合わせる」
    [InlineData(null, "fr-FR", "en")]
    public void Resolve_uses_setting_then_windows_language(string? setting, string windows, string expected)
    {
        Assert.Equal(expected, UiLanguage.Resolve(setting, CultureInfo.GetCultureInfo(windows)).Name);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" en ", "en")]
    [InlineData("Ja", "ja")]
    [InlineData("ja-JP", "")]
    [InlineData("fr", "")]
    public void Normalize_keeps_only_known_values(string? value, string expected)
    {
        Assert.Equal(expected, UiLanguage.Normalize(value));
    }

    [Fact]
    public void Language_names_are_written_in_their_own_language()
    {
        using (new UiCultureScope("en"))
        {
            Assert.Equal("English", UiLanguage.DisplayName(UiLanguage.English));
            Assert.Equal("日本語", UiLanguage.DisplayName(UiLanguage.Japanese));
            Assert.Equal(English[nameof(Strings.LanguageSystem)], UiLanguage.DisplayName(UiLanguage.System));
        }
        using (new UiCultureScope("ja"))
        {
            Assert.Equal("English", UiLanguage.DisplayName(UiLanguage.English));
            Assert.Equal("日本語", UiLanguage.DisplayName(UiLanguage.Japanese));
            Assert.Equal(Japanese[nameof(Strings.LanguageSystem)], UiLanguage.DisplayName(UiLanguage.System));
        }
    }

    [Fact]
    public void Get_returns_text_in_the_requested_language()
    {
        using var _ = new UiCultureScope("ja");
        Assert.Equal(English[nameof(Strings.RestartPrompt)], UiLanguage.Get(nameof(Strings.RestartPrompt), CultureInfo.GetCultureInfo("en")));
        Assert.Equal(Japanese[nameof(Strings.RestartPrompt)], UiLanguage.Get(nameof(Strings.RestartPrompt), CultureInfo.GetCultureInfo("ja")));
    }

    [Fact]
    public void Core_messages_follow_the_ui_language()
    {
        using (new UiCultureScope("en"))
        {
            Assert.Equal("Copy to clipboard", QuickActionSettings.Describe(QuickAction.Copy));
            Assert.Equal("Not assigned", ReleaseKeys.Describe(ReleaseKey.None));
            Assert.Equal("Unknown variable: {x}", FileNameTemplate.Validate("{x}"));
            Assert.Equal("4-digit year", FileNameTemplate.Tokens[0].Description);
            Assert.StartsWith("Combine the key with", Shortcut.Describe(ShortcutValidation.RequiresModifier));
        }
        using (new UiCultureScope("ja"))
        {
            Assert.Equal("クリップボードにコピー", QuickActionSettings.Describe(QuickAction.Copy));
            Assert.Equal("割り当てない", ReleaseKeys.Describe(ReleaseKey.None));
            Assert.Equal("使用できない変数です: {x}", FileNameTemplate.Validate("{x}"));
            Assert.Equal("西暦4桁", FileNameTemplate.Tokens[0].Description);
        }
        // 英語・日本語以外は英語（既定の言語）
        using (new UiCultureScope("de-DE"))
        {
            Assert.Equal("Copy to clipboard", QuickActionSettings.Describe(QuickAction.Copy));
        }
    }

    [Fact]
    public void Normalizer_fixes_unknown_language()
    {
        var s = new AppSettings { Language = "fr" };
        var fixes = SettingsNormalizer.Normalize(s);
        Assert.Equal(UiLanguage.System, s.Language);
        Assert.Contains(fixes, f => f.StartsWith("Language:", StringComparison.Ordinal));

        var t = new AppSettings { Language = "EN" };
        Assert.DoesNotContain(SettingsNormalizer.Normalize(t), f => f.StartsWith("Language:", StringComparison.Ordinal));
        Assert.Equal(UiLanguage.English, t.Language);

        var u = new AppSettings { Language = null! };
        SettingsNormalizer.Normalize(u);
        Assert.Equal(UiLanguage.System, u.Language);
    }

    [Fact]
    public void Setting_fix_records_are_English_regardless_of_ui_language()
    {
        // error.log に書く記録は表示言語によらず英語（仕様書 §14.4）。
        using var _ = new UiCultureScope("ja");
        var s = new AppSettings { FileNameTemplate = "{bad}", Language = "fr", Editor = new EditorDefaults { Color = "x" } };
        s.QuickActions.Edit = s.QuickActions.Copy;
        var fixes = SettingsNormalizer.Normalize(s);
        Assert.True(fixes.Count >= 4);
        var japaneseChars = new Regex(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]");
        Assert.DoesNotContain(fixes, f => japaneseChars.IsMatch(f));
    }

    [Fact]
    public void Default_language_follows_windows()
    {
        Assert.Equal(UiLanguage.System, new AppSettings().Language);
    }

    [Theory]
    [InlineData("ja", "https://www.iwanaga.net/areashot/manual/")]
    [InlineData("ja-JP", "https://www.iwanaga.net/areashot/manual/")]
    [InlineData("en", "https://www.iwanaga.net/areashot/en/manual/")]
    [InlineData("en-US", "https://www.iwanaga.net/areashot/en/manual/")]
    [InlineData("fr-FR", "https://www.iwanaga.net/areashot/en/manual/")]
    public void Manual_link_follows_ui_language(string culture, string expected)
    {
        // 取扱説明書（Web）は表示言語の版を開く。日本語以外は英語（仕様書 §14.5）。
        Assert.Equal(expected, AppLinks.Manual(CultureInfo.GetCultureInfo(culture)));
    }
}
