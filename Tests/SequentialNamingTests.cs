using AreaShot.Core.Naming;
using AreaShot.Core.Output;
using AreaShot.Core.Settings;

namespace AreaShot.Tests;

/// <summary>連番のファイル名（仕様書 §12.2。V1.3.0）。</summary>
public sealed class SequentialFileNameTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("areashot-seq-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(params string[] names)
    {
        foreach (var name in names) File.WriteAllBytes(Path.Combine(_dir, name), [1]);
    }

    [Theory]
    [InlineData("001.png", 1)]
    [InlineData("1.png", 1)]
    [InlineData("012_設定を開く.png", 12)]
    [InlineData("003_.jpg", 3)]
    [InlineData("004.JPEG", 4)]
    [InlineData("005_a_b.jpe", 5)]
    [InlineData("999999.png", 999999)]
    [InlineData("003_2.png", 3)] // 説明が "2" の名前として数える
    public void Numbered_images_are_recognized(string name, int expected)
    {
        Assert.Equal(expected, SequentialFileName.TryParseNumber(name));
    }

    [Theory]
    [InlineData("261013153000_capture.png")] // 日時のファイル名（12 桁）は連番と見なさない
    [InlineData("20261013_153000.png")]
    [InlineData("1000000.png")] // 7 桁以上
    [InlineData("001.bmp")]
    [InlineData("001.png.tmp")]
    [InlineData("a001.png")]
    [InlineData("001-a.png")]
    [InlineData("001 (2).png")]
    [InlineData("０１２.png")] // 全角数字
    [InlineData("_001.png")]
    [InlineData(".png")]
    [InlineData("~AreaShot-0A1B2C3D.tmp")]
    public void Other_names_are_ignored(string name)
    {
        Assert.Null(SequentialFileName.TryParseNumber(name));
    }

    [Fact]
    public void Next_number_is_max_plus_one_and_gaps_are_not_filled()
    {
        Assert.Equal(1, SequentialFileName.NextNumber([]));
        Assert.Equal(6, SequentialFileName.NextNumber(["001.png", "005_x.jpg", "003.png", "memo.txt", "260101120000_capture.png"]));
    }

    [Fact]
    public void Png_jpeg_and_both_naming_modes_share_the_numbers()
    {
        Touch("001.png", "002_開く.jpg", "003.jpeg");
        Assert.Equal(4, SequentialFileName.NextNumberIn(_dir));
    }

    [Fact]
    public void Missing_folder_starts_at_one()
    {
        Assert.Equal(1, SequentialFileName.NextNumberIn(Path.Combine(_dir, "none")));
    }

    [Fact]
    public void Deleting_the_largest_number_reuses_it()
    {
        Touch("001.png", "002.png");
        File.Delete(Path.Combine(_dir, "002.png"));
        Assert.Equal(2, SequentialFileName.NextNumberIn(_dir));
    }

    [Theory]
    [InlineData(1, "001")]
    [InlineData(42, "042")]
    [InlineData(999, "999")]
    [InlineData(1000, "1000")]
    public void Numbers_have_at_least_three_digits(int number, string expected)
    {
        Assert.Equal(expected, SequentialFileName.FormatNumber(number));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  設定を開く  ", "設定を開く")]
    [InlineData("a/b:c*d?e\"f<g>h|i\\j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("行1\n行2\t", "行1_行2")]
    [InlineData("末尾...", "末尾")]
    [InlineData("末尾. . ", "末尾")]
    public void Description_is_cleaned(string? input, string expected)
    {
        Assert.Equal(expected, SequentialFileName.CleanDescription(input));
    }

    [Theory]
    [InlineData(3, null, "003")]
    [InlineData(3, "", "003")]
    [InlineData(3, " 設定を開く ", "003_設定を開く")]
    [InlineData(1000, "x", "1000_x")]
    public void Base_name_joins_number_and_description(int number, string? description, string expected)
    {
        Assert.Equal(expected, SequentialFileName.BaseName(number, description));
    }

    [Fact]
    public void Too_long_names_are_rejected_without_truncation()
    {
        var ok = "001_" + new string('a', 200) + ".png";
        Assert.Null(SequentialFileName.Validate(@"C:\a", 1, ok));
        var longName = "001_" + new string('a', 252) + ".png"; // 260 文字
        Assert.NotNull(SequentialFileName.Validate(@"C:\a", 1, longName));
        var deepFolder = @"C:\" + new string('d', 240);
        Assert.NotNull(SequentialFileName.Validate(deepFolder, 1, "001_" + new string('a', 20) + ".png"));
    }

    [Fact]
    public void Number_above_the_limit_is_rejected()
    {
        Assert.Null(SequentialFileName.Validate(@"C:\a", SequentialFileName.MaxNumber, "999999.png"));
        Assert.NotNull(SequentialFileName.Validate(@"C:\a", SequentialFileName.MaxNumber + 1, "1000000.png"));
    }

    [Fact]
    public void Preview_uses_the_folder_and_the_description()
    {
        Touch("001.png", "002_a.png");
        var name = SequentialFileName.Preview(_dir, "設定を開く", ".jpg");
        Assert.Equal(3, name.Number);
        Assert.Equal("003_設定を開く.jpg", name.FileName);
        Assert.Null(name.Error);
    }

    [Fact]
    public void Saving_takes_the_next_number_when_a_file_appears_just_before_the_move()
    {
        // 保存直前に採番し直す（ImageOutputService と同じ組み合わせ）。移す前に同じ番号のファイルができても上書きしない。
        Touch("001.png");
        var calls = 0;
        string Choose()
        {
            var path = Path.Combine(_dir, SequentialFileName.Preview(_dir, "x", ".png").FileName);
            if (calls++ == 0) File.WriteAllBytes(path, [7]); // 別の保存が先に 002 を作った
            return path;
        }
        var saved = SafeFileWriter.WriteNew(_dir, Choose, s => s.Write([1, 2, 3]));
        Assert.Equal("003_x.png", Path.GetFileName(saved));
        Assert.Equal([7], File.ReadAllBytes(Path.Combine(_dir, "002_x.png"))); // 既存の画像はそのまま
        Assert.Equal([1, 2, 3], File.ReadAllBytes(saved));
    }
}

/// <summary>ファイル名の付け方・作業用フォルダの設定（仕様書 §14。V1.3.0）。</summary>
public sealed class NamingSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("areashot-naming-").FullName;
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Old_settings_file_keeps_the_template_naming_and_no_work_folder()
    {
        // V1.2.0 の設定ファイル（NamingMode・WorkFolder が無い）。
        File.WriteAllText(FilePath, """{ "SchemaVersion": 1, "SaveFolder": "D:\\shots", "FileNameTemplate": "{yyyy}_{MM}", "ImageFormat": "Png" }""");
        var result = new SettingsStore(FilePath).Load();
        var s = result.Settings;
        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Fixes);
        Assert.Equal(FileNamingMode.DateTemplate, s.NamingMode);
        Assert.Equal("", s.WorkFolder);
        Assert.Equal(@"D:\shots", s.ResolveSaveFolder());
        Assert.Equal(@"D:\shots", s.ResolveWorkFolder());
        Assert.Equal(@"D:\shots", s.ResolveTargetFolder());
        Assert.Equal("{yyyy}_{MM}", s.FileNameTemplate);
    }

    [Fact]
    public void Naming_mode_and_work_folder_roundtrip_as_text()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { NamingMode = FileNamingMode.SequentialWithDescription, WorkFolder = @"C:\手順A\images" });
        Assert.Contains("\"SequentialWithDescription\"", File.ReadAllText(FilePath));
        var s = store.Load().Settings;
        Assert.Equal(FileNamingMode.SequentialWithDescription, s.NamingMode);
        Assert.Equal(@"C:\手順A\images", s.WorkFolder);
    }

    [Fact]
    public void Target_folder_depends_on_the_naming_mode()
    {
        var s = new AppSettings { SaveFolder = @"C:\AreaShot", WorkFolder = @"C:\手順A" };
        Assert.Equal(@"C:\AreaShot", s.ResolveTargetFolder()); // ファイル名テンプレートは作業用フォルダの影響を受けない
        s.NamingMode = FileNamingMode.Sequential;
        Assert.Equal(@"C:\手順A", s.ResolveTargetFolder());
        s.NamingMode = FileNamingMode.SequentialWithDescription;
        Assert.Equal(@"C:\手順A", s.ResolveTargetFolder());
        Assert.Equal(@"C:\手順A", s.ResolveWorkFolder());
    }

    [Fact]
    public void Unset_work_folder_follows_the_save_folder()
    {
        var s = new AppSettings { SaveFolder = @"C:\AreaShot", NamingMode = FileNamingMode.Sequential };
        Assert.Equal(@"C:\AreaShot", s.ResolveTargetFolder());
        s.SaveFolder = @"D:\Other";
        Assert.Equal(@"D:\Other", s.ResolveWorkFolder());
    }

    [Fact]
    public void Invalid_naming_mode_number_is_normalized()
    {
        File.WriteAllText(FilePath, """{ "NamingMode": 9, "WorkFolder": null }""");
        var result = new SettingsStore(FilePath).Load();
        Assert.Equal(FileNamingMode.DateTemplate, result.Settings.NamingMode);
        Assert.Equal("", result.Settings.WorkFolder);
        Assert.Contains(result.Fixes, f => f.StartsWith("NamingMode"));
    }

    [Fact]
    public void Clone_copies_the_new_settings()
    {
        var s = new AppSettings { NamingMode = FileNamingMode.Sequential, WorkFolder = @"C:\w" };
        var copy = s.Clone();
        copy.WorkFolder = @"C:\x";
        Assert.Equal(FileNamingMode.Sequential, copy.NamingMode);
        Assert.Equal(@"C:\w", s.WorkFolder);
    }

    [Fact]
    public void Every_naming_mode_has_a_display_name()
    {
        Assert.Equal(3, FileNamingModes.All.Count);
        Assert.Equal(3, FileNamingModes.All.Select(FileNamingModes.Describe).Distinct().Count());
    }
}
