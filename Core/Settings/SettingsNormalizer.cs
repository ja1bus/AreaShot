using AreaShot.Core.Annotations;
using AreaShot.Core.Input;
using AreaShot.Core.Localization;
using AreaShot.Core.Naming;

namespace AreaShot.Core.Settings;

/// <summary>
/// 読み込んだ設定の補正（仕様書 §14.3）。範囲外・不正な値は既定値またはプリセットの近い値へ直し、直した項目を返す。
/// </summary>
public static class SettingsNormalizer
{
    public static IReadOnlyList<string> Normalize(AppSettings s)
    {
        var fixes = new List<string>();

        s.SaveFolder ??= "";
        if (FileNameTemplate.Validate(s.FileNameTemplate) is not null)
        {
            fixes.Add($"FileNameTemplate: '{s.FileNameTemplate}' → default");
            s.FileNameTemplate = FileNameTemplate.Default;
        }
        if (!Enum.IsDefined(s.ImageFormat))
        {
            fixes.Add($"ImageFormat: {s.ImageFormat} → Png");
            s.ImageFormat = ImageFormatKind.Png;
        }
        if (s.JpegQuality is < 1 or > 100)
        {
            var fixedValue = s.JpegQuality < 1 ? AppSettings.DefaultJpegQuality : 100;
            fixes.Add($"JpegQuality: {s.JpegQuality} → {fixedValue}");
            s.JpegQuality = fixedValue;
        }
        // 空は「未割当」として有効。読めない表記は既定へ戻す。
        if (!string.IsNullOrEmpty(s.CaptureHotkey))
        {
            if (!Shortcut.TryParse(s.CaptureHotkey, out var sc) || sc.Validate() != ShortcutValidation.Ok)
            {
                fixes.Add($"CaptureHotkey: '{s.CaptureHotkey}' → {Shortcut.DefaultCapture}");
                s.CaptureHotkey = Shortcut.DefaultCapture.ToString();
            }
            else
            {
                s.CaptureHotkey = sc.ToString(); // 表記をそろえる
            }
        }
        s.CaptureHotkey ??= "";

        s.QuickActions ??= new QuickActionSettings();
        var q = s.QuickActions;
        var defaults = new QuickActionSettings();
        var used = new HashSet<ReleaseKey>();
        foreach (var action in Enum.GetValues<QuickAction>())
        {
            var key = q.KeyOf(action);
            if (!Enum.IsDefined(key))
            {
                fixes.Add($"QuickActions.{action}: {key} → {defaults.KeyOf(action)}");
                key = defaults.KeyOf(action);
                q.SetKey(action, key);
            }
            // 同じキーが重なっていたら、頻度の低い操作の方を未割当にする。
            if (key != ReleaseKey.None && !used.Add(key))
            {
                fixes.Add($"QuickActions.{action}: {key} duplicates another action → None");
                q.SetKey(action, ReleaseKey.None);
            }
        }

        s.Editor ??= new EditorDefaults();
        var e = s.Editor;
        if (!ArgbColor.TryParse(e.Color, out _))
        {
            fixes.Add($"Editor.Color: '{e.Color}' → default");
            e.Color = EditorPresets.DefaultColor.ToString();
        }
        var width = EditorPresets.Nearest(EditorPresets.StrokeWidths, e.StrokeWidth);
        if (width != e.StrokeWidth) { fixes.Add($"Editor.StrokeWidth: {e.StrokeWidth} → {width}"); e.StrokeWidth = width; }
        var size = EditorPresets.Nearest(EditorPresets.FontSizes, e.FontSize);
        if (size != e.FontSize) { fixes.Add($"Editor.FontSize: {e.FontSize} → {size}"); e.FontSize = size; }
        if (string.IsNullOrWhiteSpace(e.FontFamily)) e.FontFamily = EditorPresets.DefaultFontFamily;
        // フォントが実在するかは UI 側（FontCatalog）で確認して代替する。

        // 大文字・空白の違いは黙って直し、知らない言語だけ記録する。
        var language = UiLanguage.Normalize(s.Language);
        if (language == UiLanguage.System && !string.IsNullOrWhiteSpace(s.Language))
            fixes.Add($"Language: '{s.Language}' → default (Windows display language)");
        s.Language = language;

        s.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return fixes;
    }
}
