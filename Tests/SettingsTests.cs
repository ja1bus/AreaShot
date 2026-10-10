using AreaShot.Core.Annotations;
using AreaShot.Core.Input;
using AreaShot.Core.Naming;
using AreaShot.Core.Settings;

namespace AreaShot.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("areashot-settings-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var result = new SettingsStore(FilePath).Load();
        Assert.Equal(SettingsLoadStatus.Missing, result.Status);
        var s = result.Settings;
        Assert.Equal(FileNameTemplate.Default, s.FileNameTemplate);
        Assert.Equal(ImageFormatKind.Png, s.ImageFormat);
        Assert.Equal(90, s.JpegQuality);
        Assert.Equal("Ctrl+Alt+S", s.CaptureHotkey);
        Assert.True(s.ShowSaveNotification);
        Assert.Equal("Meiryo", s.Editor.FontFamily);
        Assert.Equal(3, s.Editor.StrokeWidth);
        Assert.Equal(20, s.Editor.FontSize);
        Assert.Equal(EditorPresets.DefaultColor.ToString(), s.Editor.Color);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var store = new SettingsStore(FilePath);
        var s = new AppSettings
        {
            SaveFolder = @"D:\Shots",
            FileNameTemplate = "{yyyy}{MM}{dd}_shot",
            ImageFormat = ImageFormatKind.Jpeg,
            JpegQuality = 75,
            CaptureHotkey = "Ctrl+Shift+F9",
            ShowSaveNotification = false,
            Language = "en",
            Editor = new EditorDefaults { Color = "#FF000000", StrokeWidth = 5, FontFamily = "MS Gothic", FontSize = 32 },
        };
        store.Save(s);
        var loaded = store.Load();
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        Assert.Empty(loaded.Fixes);
        var r = loaded.Settings;
        Assert.Equal(@"D:\Shots", r.SaveFolder);
        Assert.Equal("{yyyy}{MM}{dd}_shot", r.FileNameTemplate);
        Assert.Equal(ImageFormatKind.Jpeg, r.ImageFormat);
        Assert.Equal(75, r.JpegQuality);
        Assert.Equal("Ctrl+Shift+F9", r.CaptureHotkey);
        Assert.False(r.ShowSaveNotification);
        Assert.Equal("en", r.Language);
        Assert.Equal("#FF000000", r.Editor.Color);
        Assert.Equal(5, r.Editor.StrokeWidth);
        Assert.Equal("MS Gothic", r.Editor.FontFamily);
        Assert.Equal(32, r.Editor.FontSize);
    }

    [Fact]
    public void Image_format_is_saved_as_text()
    {
        new SettingsStore(FilePath).Save(new AppSettings { ImageFormat = ImageFormatKind.Jpeg });
        Assert.Contains("\"Jpeg\"", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"ImageFormat\": \"Bmp\"}")]
    public void Corrupt_file_gives_defaults_and_backup(string content)
    {
        File.WriteAllText(FilePath, content);
        var result = new SettingsStore(FilePath).Load();
        Assert.Equal(SettingsLoadStatus.Corrupt, result.Status);
        Assert.Equal(FileNameTemplate.Default, result.Settings.FileNameTemplate);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Equal(content, File.ReadAllText(result.BackupPath!));
    }

    [Fact]
    public void Out_of_range_values_are_normalized()
    {
        File.WriteAllText(FilePath, """
            {
              "FileNameTemplate": "{bad}",
              "JpegQuality": 500,
              "CaptureHotkey": "Ctrl+Nope",
              "Editor": { "Color": "red", "StrokeWidth": 4, "FontSize": 100, "FontFamily": "" }
            }
            """);
        var result = new SettingsStore(FilePath).Load();
        var s = result.Settings;
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(FileNameTemplate.Default, s.FileNameTemplate);
        Assert.Equal(100, s.JpegQuality);
        Assert.Equal("Ctrl+Alt+S", s.CaptureHotkey);
        Assert.Equal(EditorPresets.DefaultColor.ToString(), s.Editor.Color);
        Assert.Contains(s.Editor.StrokeWidth, EditorPresets.StrokeWidths);
        Assert.Equal(48, s.Editor.FontSize);
        Assert.Equal("Meiryo", s.Editor.FontFamily);
        Assert.NotEmpty(result.Fixes);
    }

    [Fact]
    public void Missing_properties_use_defaults()
    {
        File.WriteAllText(FilePath, """{ "ImageFormat": "Jpeg" }""");
        var s = new SettingsStore(FilePath).Load().Settings;
        Assert.Equal(ImageFormatKind.Jpeg, s.ImageFormat);
        Assert.Equal(90, s.JpegQuality);
        Assert.Equal("Ctrl+Alt+S", s.CaptureHotkey);
    }

    [Fact]
    public void Empty_hotkey_means_unassigned_and_is_kept()
    {
        File.WriteAllText(FilePath, """{ "CaptureHotkey": "" }""");
        Assert.Equal("", new SettingsStore(FilePath).Load().Settings.CaptureHotkey);
    }

    [Fact]
    public void Save_creates_directory()
    {
        var nested = Path.Combine(_dir, "a", "b", "settings.json");
        new SettingsStore(nested).Save(new AppSettings());
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Empty_save_folder_resolves_to_pictures_areashot()
    {
        var s = new AppSettings();
        Assert.Equal(AppSettings.DefaultSaveFolder, s.ResolveSaveFolder());
        Assert.EndsWith("AreaShot", s.ResolveSaveFolder());
    }
}

public class ShortcutTests
{
    [Fact]
    public void Default_is_ctrl_alt_s()
    {
        Assert.Equal("Ctrl+Alt+S", Shortcut.DefaultCapture.ToString());
        Assert.Equal(ShortcutValidation.Ok, Shortcut.DefaultCapture.Validate());
    }

    [Theory]
    [InlineData("Ctrl+Alt+S", "Ctrl+Alt+S")]
    [InlineData("alt+ctrl+s", "Ctrl+Alt+S")]
    [InlineData("Ctrl+Shift+F9", "Ctrl+Shift+F9")]
    [InlineData("PrintScreen", "PrintScreen")]
    [InlineData("Win+Shift+VK_BA", "Shift+Win+VK_BA")]
    public void Parse_and_format(string input, string expected)
    {
        Assert.True(Shortcut.TryParse(input, out var sc));
        Assert.Equal(expected, sc.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Hyper+S")]
    [InlineData("Ctrl+Nope")]
    public void Parse_rejects_invalid(string input)
    {
        Assert.False(Shortcut.TryParse(input, out _));
    }

    [Theory]
    [InlineData("S", ShortcutValidation.RequiresModifier)]
    [InlineData("Shift+S", ShortcutValidation.RequiresModifier)]
    [InlineData("F12", ShortcutValidation.Ok)]
    [InlineData("PrintScreen", ShortcutValidation.Ok)]
    [InlineData("Win+S", ShortcutValidation.Ok)]
    public void Validation(string input, ShortcutValidation expected)
    {
        Assert.True(Shortcut.TryParse(input, out var sc));
        Assert.Equal(expected, sc.Validate());
    }
}

public sealed class QuickActionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("areashot-quick-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Defaults_follow_frequency_order()
    {
        var q = new AppSettings().QuickActions;
        Assert.True(q.Enabled);
        Assert.True(q.ShowHint);
        Assert.Equal(ReleaseKey.Ctrl, q.Copy);
        Assert.Equal(ReleaseKey.Shift, q.Edit);
        Assert.Equal(ReleaseKey.CtrlShift, q.Save);
        Assert.Equal(ReleaseKey.Alt, q.SaveAs);
        Assert.Empty(q.Duplicates());
    }

    [Fact]
    public void Old_settings_file_without_quick_actions_gets_defaults()
    {
        File.WriteAllText(FilePath, """{ "ImageFormat": "Jpeg" }""");
        var result = new SettingsStore(FilePath).Load();
        Assert.Empty(result.Fixes);
        Assert.Equal(ReleaseKey.Ctrl, result.Settings.QuickActions.Copy);
    }

    [Fact]
    public void Roundtrip_keeps_assignments_and_saves_as_text()
    {
        var store = new SettingsStore(FilePath);
        var s = new AppSettings
        {
            QuickActions = new QuickActionSettings
            {
                Enabled = false, ShowHint = false,
                Copy = ReleaseKey.Shift, Edit = ReleaseKey.None, Save = ReleaseKey.Ctrl, SaveAs = ReleaseKey.None,
            },
        };
        store.Save(s);
        Assert.Contains("\"Shift\"", File.ReadAllText(FilePath)); // 数値でなく名前で保存する
        var loaded = store.Load();
        Assert.Empty(loaded.Fixes);
        var q = loaded.Settings.QuickActions;
        Assert.False(q.Enabled);
        Assert.False(q.ShowHint);
        Assert.Equal(ReleaseKey.Shift, q.Copy);
        Assert.Equal(ReleaseKey.None, q.Edit);
        Assert.Equal(ReleaseKey.Ctrl, q.Save);
        Assert.Equal(ReleaseKey.None, q.SaveAs);
    }

    [Fact]
    public void Duplicates_are_cleared_on_the_less_frequent_action()
    {
        File.WriteAllText(FilePath, """{ "QuickActions": { "Copy": "Ctrl", "Edit": "Ctrl", "Save": 99, "SaveAs": "Shift" } }""");
        var result = new SettingsStore(FilePath).Load();
        var q = result.Settings.QuickActions;
        Assert.Equal(ReleaseKey.Ctrl, q.Copy);
        Assert.Equal(ReleaseKey.None, q.Edit);        // Copy と重複
        Assert.Equal(ReleaseKey.CtrlShift, q.Save);   // 不正な値 → 既定
        Assert.Equal(ReleaseKey.Shift, q.SaveAs);
        Assert.Equal(2, result.Fixes.Count);
    }

    [Theory]
    [InlineData(KeyModifiers.None, ReleaseKey.None)]
    [InlineData(KeyModifiers.Control, ReleaseKey.Ctrl)]
    [InlineData(KeyModifiers.Shift, ReleaseKey.Shift)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, ReleaseKey.CtrlShift)]
    [InlineData(KeyModifiers.Alt, ReleaseKey.Alt)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, ReleaseKey.None)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Win, ReleaseKey.None)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, ReleaseKey.None)]
    public void Modifiers_map_only_to_listed_keys(KeyModifiers modifiers, ReleaseKey expected)
    {
        Assert.Equal(expected, ReleaseKeys.FromModifiers(modifiers));
    }

    [Fact]
    public void Resolve_uses_assignment_and_respects_off()
    {
        var q = new QuickActionSettings { SaveAs = ReleaseKey.None };
        Assert.Equal(QuickAction.Copy, q.Resolve(ReleaseKey.Ctrl));
        Assert.Equal(QuickAction.Edit, q.Resolve(ReleaseKey.Shift));
        Assert.Equal(QuickAction.Save, q.Resolve(ReleaseKey.CtrlShift));
        Assert.Null(q.Resolve(ReleaseKey.Alt));   // 割り当てない
        Assert.Null(q.Resolve(ReleaseKey.None));  // キーなし → ダイアログ
        q.Enabled = false;
        Assert.Null(q.Resolve(ReleaseKey.Ctrl));
        Assert.Empty(q.Assigned());
    }

    [Fact]
    public void Clone_is_independent()
    {
        var s = new AppSettings();
        var c = s.Clone();
        c.QuickActions.Copy = ReleaseKey.None;
        Assert.Equal(ReleaseKey.Ctrl, s.QuickActions.Copy);
    }
}
