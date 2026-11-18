using System.Text.Json;
using AreaShot.Core.Geometry;
using AreaShot.Core.Input;
using AreaShot.Core.Naming;
using AreaShot.Core.Output;
using AreaShot.Core.Settings;

namespace AreaShot.Tests;

/// <summary>V1.4.0 の設定・ログ（verification.md T-17・T-18・T-32・T-45・T-47・T-48・T-56）。利用者の設定ファイルは使わない。</summary>
public sealed class V140SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("areashot-v140-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsLoadResult LoadJson(string json)
    {
        File.WriteAllText(FilePath, json);
        return new SettingsStore(FilePath).Load();
    }

    private void AssertNotBroken(SettingsLoadResult r)
    {
        Assert.Equal(SettingsLoadStatus.Loaded, r.Status);
        Assert.Null(r.BackupPath);
        Assert.Empty(Directory.GetFiles(_dir, "settings.json.broken-*"));
    }

    // ---- 前回と同じ範囲のショートカット（AC-3） ----

    [Fact]
    public void T17_repeat_capture_hotkey_is_normalized()
    {
        Assert.Equal("Ctrl+Alt+R", Shortcut.DefaultRepeatCapture.ToString());

        var a = LoadJson("{\"CaptureHotkey\":\"Ctrl+Alt+S\"}");
        AssertNotBroken(a);
        Assert.Equal("Ctrl+Alt+R", a.Settings.RepeatCaptureHotkey);
        Assert.DoesNotContain(a.Fixes, f => f.Contains("RepeatCaptureHotkey"));

        var b = LoadJson("{\"RepeatCaptureHotkey\":\"\"}");
        Assert.Equal("", b.Settings.RepeatCaptureHotkey);

        foreach (var bad in new[] { "Ctrl+Nope", "Shift+A" })
        {
            var c = LoadJson($"{{\"RepeatCaptureHotkey\":\"{bad}\"}}");
            AssertNotBroken(c);
            Assert.Equal("Ctrl+Alt+R", c.Settings.RepeatCaptureHotkey);
            Assert.Contains(c.Fixes, f => f.Contains("RepeatCaptureHotkey"));
        }

        var e = LoadJson("{\"CaptureHotkey\":\"Ctrl+Alt+S\",\"RepeatCaptureHotkey\":\"Ctrl+Alt+S\"}");
        Assert.Equal("Ctrl+Alt+S", e.Settings.CaptureHotkey);
        Assert.Equal("", e.Settings.RepeatCaptureHotkey);
        Assert.Contains(e.Fixes, f => f.Contains("RepeatCaptureHotkey"));

        var f = LoadJson("{\"CaptureHotkey\":\"Ctrl+Alt+R\"}");
        Assert.Equal("", f.Settings.RepeatCaptureHotkey);
        Assert.Contains(f.Fixes, x => x.Contains("RepeatCaptureHotkey"));

        var g = LoadJson("{\"CaptureHotkey\":\"Ctrl+Alt+S\",\"RepeatCaptureHotkey\":\"Alt+Ctrl+S\"}");
        Assert.Equal("", g.Settings.RepeatCaptureHotkey);

        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { RepeatCaptureHotkey = "Ctrl+Shift+F5" });
        using (var doc = JsonDocument.Parse(File.ReadAllText(FilePath)))
            Assert.Equal("Ctrl+Shift+F5", doc.RootElement.GetProperty("RepeatCaptureHotkey").GetString());
        Assert.Equal("Ctrl+Shift+F5", store.Load().Settings.RepeatCaptureHotkey);
    }

    [Fact]
    public void T18_repeat_region_intersects_the_virtual_screen()
    {
        var screen = new PixelRect(-1920, 0, 3840, 1080);
        Assert.Equal(new PixelRect(100, 100, 200, 100), new PixelRect(100, 100, 200, 100).Intersect(screen));
        Assert.Equal(new PixelRect(1800, 1000, 120, 80), new PixelRect(1800, 1000, 300, 200).Intersect(screen));
        Assert.True(new PixelRect(2000, 0, 10, 10).Intersect(screen).IsEmpty);
        Assert.True(new PixelRect(1920, 0, 10, 10).Intersect(screen).IsEmpty);
        Assert.Equal(new PixelRect(-1920, 0, 1, 1), new PixelRect(-1920, 0, 1, 1).Intersect(screen));
    }

    // ---- 縁取りの既定値（AC-5） ----

    [Fact]
    public void T32_text_outline_default_is_saved()
    {
        var a = LoadJson("{\"Editor\":{\"Color\":\"#FFE60012\",\"StrokeWidth\":3,\"FontFamily\":\"Meiryo\",\"FontSize\":20}}");
        AssertNotBroken(a);
        Assert.False(a.Settings.Editor.TextOutline);
        Assert.Empty(a.Fixes);

        var store = new SettingsStore(FilePath);
        var s = new AppSettings();
        s.Editor.TextOutline = true;
        store.Save(s);
        using (var doc = JsonDocument.Parse(File.ReadAllText(FilePath)))
            Assert.True(doc.RootElement.GetProperty("Editor").GetProperty("TextOutline").GetBoolean());
        Assert.True(store.Load().Settings.Editor.TextOutline);
    }

    // ---- error.log の上限（AC-8） ----

    [Fact]
    public void T45_log_rotation()
    {
        var log = Path.Combine(_dir, "error.log");
        var rotated = log + ".1";
        const long limit = 1048576;

        Assert.False(LogRotation.RotateIfLarger(log, limit));
        Assert.False(File.Exists(log) || File.Exists(rotated));

        foreach (var size in new[] { limit - 1, limit })
        {
            File.WriteAllBytes(log, new byte[size]);
            Assert.False(LogRotation.RotateIfLarger(log, limit));
            Assert.False(File.Exists(rotated));
            Assert.Equal(size, new FileInfo(log).Length);
        }

        var big = new byte[limit + 1];
        new Random(3).NextBytes(big);
        File.WriteAllBytes(log, big);
        Assert.True(LogRotation.RotateIfLarger(log, limit));
        Assert.False(File.Exists(log));
        Assert.Equal(big, File.ReadAllBytes(rotated));

        File.WriteAllText(rotated, "old");
        File.WriteAllBytes(log, big);
        Assert.True(LogRotation.RotateIfLarger(log, limit));
        Assert.Equal(big, File.ReadAllBytes(rotated));

        File.WriteAllText(rotated, "old");
        File.WriteAllBytes(log, big);
        using (new FileStream(rotated, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Windows では UnauthorizedAccessException になる（ErrorLog は IOException と合わせて握る）。
            Assert.ThrowsAny<Exception>(() => LogRotation.RotateIfLarger(log, limit));
        }
        Assert.Equal(big, File.ReadAllBytes(log));
        Assert.Equal("old", File.ReadAllText(rotated));
        Assert.Equal(LogRotation.DefaultLimitBytes, limit);
    }

    // ---- 設定の列挙値（AC-9） ----

    [Fact]
    public void T47_unknown_enum_values_fall_back_per_item()
    {
        var a = LoadJson("{\"ImageFormat\":\"Bmp\",\"JpegQuality\":70}");
        AssertNotBroken(a);
        Assert.Equal(ImageFormatKind.Png, a.Settings.ImageFormat);
        Assert.Equal(70, a.Settings.JpegQuality);
        Assert.Contains(a.Fixes, f => f.Contains("ImageFormat") && f.Contains("'Bmp'") && f.Contains("Png"));

        foreach (var json in new[] { "{\"ImageFormat\":\"jpeg\"}", "{\"imageformat\":\"Jpeg\"}", "{\"ImageFormat\":1}" })
        {
            var r = LoadJson(json);
            AssertNotBroken(r);
            Assert.Equal(ImageFormatKind.Jpeg, r.Settings.ImageFormat);
            Assert.Empty(r.Fixes);
        }

        var d = LoadJson("{\"NamingMode\":\"Foo\"}");
        AssertNotBroken(d);
        Assert.Equal(FileNamingMode.DateTemplate, d.Settings.NamingMode);
        Assert.Contains(d.Fixes, f => f.Contains("NamingMode") && f.Contains("'Foo'"));
        Assert.Equal(FileNamingMode.Sequential, LoadJson("{\"NamingMode\":\"sequential\"}").Settings.NamingMode);

        foreach (var value in new[] { "true", "{}", "[]", "null" })
        {
            var g = LoadJson($"{{\"ImageFormat\":{value}}}");
            AssertNotBroken(g);
            Assert.Equal(ImageFormatKind.Png, g.Settings.ImageFormat);
            Assert.Contains(g.Fixes, f => f.Contains("ImageFormat"));
        }

        var h = LoadJson("{\"QuickActions\":{\"Copy\":\"Win\",\"Edit\":\"Shift\"}}");
        AssertNotBroken(h);
        Assert.Equal(ReleaseKey.Ctrl, h.Settings.QuickActions.Copy);
        Assert.Equal(ReleaseKey.Shift, h.Settings.QuickActions.Edit);
        Assert.Equal(ReleaseKey.CtrlShift, h.Settings.QuickActions.Save);
        Assert.Equal(ReleaseKey.Alt, h.Settings.QuickActions.SaveAs);
        Assert.Contains(h.Fixes, f => f.Contains("QuickActions.Copy") && f.Contains("'Win'"));

        var i = LoadJson("{\"QuickActions\":{\"Copy\":\"Bad\",\"Edit\":\"Ctrl\"}}");
        AssertNotBroken(i);
        Assert.Equal(ReleaseKey.Ctrl, i.Settings.QuickActions.Copy);
        Assert.Equal(ReleaseKey.None, i.Settings.QuickActions.Edit);
        Assert.True(i.Fixes.Count >= 2);

        Assert.Equal(ReleaseKey.CtrlShift, LoadJson("{\"QuickActions\":{\"Save\":\"ctrlshift\"}}").Settings.QuickActions.Save);

        var k = LoadJson("{\"ImageFormat\":\"Bmp\",\"NamingMode\":\"X\",\"FileNameTemplate\":\"{yyyy}_a\"}");
        AssertNotBroken(k);
        Assert.Equal(ImageFormatKind.Png, k.Settings.ImageFormat);
        Assert.Equal(FileNamingMode.DateTemplate, k.Settings.NamingMode);
        Assert.Equal("{yyyy}_a", k.Settings.FileNameTemplate);
        Assert.True(k.Fixes.Count >= 2);

        var l = LoadJson("{ // comment\n  \"ImageFormat\": \"Bmp\",\n}");
        AssertNotBroken(l);
        Assert.Equal(ImageFormatKind.Png, l.Settings.ImageFormat);
        Assert.Contains(l.Fixes, f => f.Contains("ImageFormat"));
    }

    /// <summary>レビュー R1: 同じキーが重なっていても破損にせず、従来どおり最後の指定を有効にする。</summary>
    [Fact]
    public void T47_duplicate_keys_keep_the_last_value()
    {
        var a = LoadJson("{\"JpegQuality\":70,\"Language\":\"ja\",\"ImageFormat\":\"Png\",\"imageformat\":\"Jpeg\"}");
        AssertNotBroken(a);
        Assert.Equal(ImageFormatKind.Jpeg, a.Settings.ImageFormat);
        Assert.Equal(70, a.Settings.JpegQuality);
        Assert.Equal("ja", a.Settings.Language);
        Assert.Empty(a.Fixes);

        var b = LoadJson("{\"JpegQuality\":70,\"Language\":\"ja\",\"ImageFormat\":\"Png\",\"ImageFormat\":\"Jpeg\"}");
        AssertNotBroken(b);
        Assert.Equal(ImageFormatKind.Jpeg, b.Settings.ImageFormat);
        Assert.Equal(70, b.Settings.JpegQuality);

        var c = LoadJson("{\"JpegQuality\":70,\"Language\":\"ja\",\"QuickActions\":{\"Copy\":\"None\",\"copy\":\"Ctrl\"}}");
        AssertNotBroken(c);
        Assert.Equal(ReleaseKey.Ctrl, c.Settings.QuickActions.Copy);
        Assert.Equal(70, c.Settings.JpegQuality);
        Assert.Equal("ja", c.Settings.Language);
        Assert.Empty(c.Fixes);

        // 列挙値以外の重複も従来どおり最後の指定。
        var d = LoadJson("{\"JpegQuality\":50,\"jpegquality\":70,\"ImageFormat\":\"Jpeg\"}");
        AssertNotBroken(d);
        Assert.Equal(70, d.Settings.JpegQuality);

        // 最後の指定が読めなければ、その項目だけ既定値（前の読める指定は使わない）。
        var e = LoadJson("{\"JpegQuality\":70,\"ImageFormat\":\"Jpeg\",\"imageformat\":\"Bmp\"}");
        AssertNotBroken(e);
        Assert.Equal(ImageFormatKind.Png, e.Settings.ImageFormat);
        Assert.Equal(70, e.Settings.JpegQuality);
        Assert.Single(e.Fixes, f => f.Contains("ImageFormat") && f.Contains("'Bmp'"));

        // 前の指定だけが読めなければ、黙って取り除いて最後の指定を使う。
        var f = LoadJson("{\"ImageFormat\":\"Bmp\",\"ImageFormat\":\"Jpeg\"}");
        AssertNotBroken(f);
        Assert.Equal(ImageFormatKind.Jpeg, f.Settings.ImageFormat);
        Assert.Empty(f.Fixes);

        // QuickActions の重複は最後のオブジェクトが有効。補正の記録もそれだけ。
        var g = LoadJson("{\"QuickActions\":{\"Copy\":\"Bad\"},\"QuickActions\":{\"Copy\":\"Alt\",\"SaveAs\":\"None\"}}");
        AssertNotBroken(g);
        Assert.Equal(ReleaseKey.Alt, g.Settings.QuickActions.Copy);
        Assert.Empty(g.Fixes);
    }

    /// <summary>レビュー R3: 1.3.3 が読めた表記（前後の空白・引用符付きの整数）は既定値に置き換えない。</summary>
    [Fact]
    public void T47_values_readable_by_1_3_3_are_kept()
    {
        var a = LoadJson("{\"JpegQuality\":70,\"Language\":\"ja\",\"ImageFormat\":\" Jpeg \",\"NamingMode\":\" Sequential \"}");
        AssertNotBroken(a);
        Assert.Equal(ImageFormatKind.Jpeg, a.Settings.ImageFormat);
        Assert.Equal(FileNamingMode.Sequential, a.Settings.NamingMode);
        Assert.Empty(a.Fixes);

        var b = LoadJson("{\"JpegQuality\":70,\"Language\":\"ja\",\"ImageFormat\":\"1\",\"NamingMode\":\"1\"}");
        AssertNotBroken(b);
        Assert.Equal(ImageFormatKind.Jpeg, b.Settings.ImageFormat);
        Assert.Equal(FileNamingMode.Sequential, b.Settings.NamingMode);
        Assert.Empty(b.Fixes);

        var c = LoadJson("{\"QuickActions\":{\"Copy\":\" alt \",\"Edit\":\"0\"}}");
        AssertNotBroken(c);
        Assert.Equal(ReleaseKey.Alt, c.Settings.QuickActions.Copy);
        Assert.Equal(ReleaseKey.None, c.Settings.QuickActions.Edit);
        Assert.DoesNotContain(c.Fixes, x => x.Contains("QuickActions.Copy") || x.Contains("QuickActions.Edit"));

        // 未知の名前は従来どおり、その項目だけ既定値にして記録する。
        var d = LoadJson("{\"JpegQuality\":70,\"ImageFormat\":\" Bmp \",\"NamingMode\":\"Sequential\"}");
        AssertNotBroken(d);
        Assert.Equal(ImageFormatKind.Png, d.Settings.ImageFormat);
        Assert.Equal(FileNamingMode.Sequential, d.Settings.NamingMode);
        Assert.Single(d.Fixes, x => x.Contains("ImageFormat") && x.Contains("' Bmp '") && x.Contains("Png"));

        // 範囲外の数値（引用符付きを含む）は SettingsNormalizer が直す（従来どおり）。
        foreach (var json in new[] { "{\"ImageFormat\":9}", "{\"ImageFormat\":\"9\"}" })
        {
            var r = LoadJson(json);
            AssertNotBroken(r);
            Assert.Equal(ImageFormatKind.Png, r.Settings.ImageFormat);
        }
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"QuickActions\":5}")]
    [InlineData("{\"QuickActions\":\"Ctrl\"}")]
    [InlineData("{\"JpegQuality\":\"abc\"}")]
    public void T48_unreadable_files_are_still_corrupt(string content)
    {
        var r = LoadJson(content);
        Assert.Equal(SettingsLoadStatus.Corrupt, r.Status);
        Assert.Equal(FileNameTemplate.Default, r.Settings.FileNameTemplate);
        Assert.NotNull(r.BackupPath);
        Assert.Equal(content, File.ReadAllText(r.BackupPath!));
    }

    // ---- 1.3.3 の設定ファイル（AC-13） ----

    [Fact]
    public void T56_settings_from_1_3_3_are_kept()
    {
        const string old = """
        {
          "SchemaVersion": 1,
          "SaveFolder": "C:\\S",
          "FileNameTemplate": "{yyyy}{MM}{dd}_x",
          "NamingMode": "Sequential",
          "WorkFolder": "C:\\W",
          "ImageFormat": "Jpeg",
          "JpegQuality": 75,
          "CaptureHotkey": "Ctrl+Shift+F9",
          "ShowSaveNotification": false,
          "QuickActions": { "Enabled": false, "Copy": "Alt", "Edit": "None", "Save": "Ctrl", "SaveAs": "Shift", "ShowHint": false },
          "Editor": { "Color": "#FF005AFF", "StrokeWidth": 5, "FontFamily": "MS Gothic", "FontSize": 32 },
          "Language": "ja"
        }
        """;
        var r = LoadJson(old);
        AssertNotBroken(r);
        Assert.Empty(r.Fixes);
        var s = r.Settings;
        Assert.Equal(@"C:\S", s.SaveFolder);
        Assert.Equal("{yyyy}{MM}{dd}_x", s.FileNameTemplate);
        Assert.Equal(FileNamingMode.Sequential, s.NamingMode);
        Assert.Equal(@"C:\W", s.WorkFolder);
        Assert.Equal(ImageFormatKind.Jpeg, s.ImageFormat);
        Assert.Equal(75, s.JpegQuality);
        Assert.Equal("Ctrl+Shift+F9", s.CaptureHotkey);
        Assert.False(s.ShowSaveNotification);
        Assert.False(s.QuickActions.Enabled);
        Assert.Equal((ReleaseKey.Alt, ReleaseKey.None, ReleaseKey.Ctrl, ReleaseKey.Shift),
            (s.QuickActions.Copy, s.QuickActions.Edit, s.QuickActions.Save, s.QuickActions.SaveAs));
        Assert.False(s.QuickActions.ShowHint);
        Assert.Equal(("#FF005AFF", 5.0, "MS Gothic", 32.0), (s.Editor.Color, s.Editor.StrokeWidth, s.Editor.FontFamily, s.Editor.FontSize));
        Assert.Equal("ja", s.Language);
        Assert.Equal("Ctrl+Alt+R", s.RepeatCaptureHotkey);
        Assert.False(s.Editor.TextOutline);

        new SettingsStore(FilePath).Save(s);
        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        var top = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        using var oldDoc = JsonDocument.Parse(old);
        var oldTop = oldDoc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(["RepeatCaptureHotkey"], top.Except(oldTop).ToList());
        var editor = doc.RootElement.GetProperty("Editor").EnumerateObject().Select(p => p.Name).ToHashSet();
        var oldEditor = oldDoc.RootElement.GetProperty("Editor").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(["TextOutline"], editor.Except(oldEditor).ToList());
    }
}
