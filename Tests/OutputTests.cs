using AreaShot.Core.Naming;
using AreaShot.Core.Output;

namespace AreaShot.Tests;

/// <summary>「名前を付けて保存」の保存先の決定（仕様書 §12.3）。</summary>
public class SaveAsPathTests
{
    [Theory]
    [InlineData(@"C:\a\shot.png", false, @"C:\a\shot.png")]
    [InlineData(@"C:\a\shot.PNG", true, @"C:\a\shot.PNG")]
    [InlineData(@"C:\a\shot.jpg", false, @"C:\a\shot.jpg")]
    [InlineData(@"C:\a\shot.Jpeg", false, @"C:\a\shot.Jpeg")]
    [InlineData(@"C:\a\shot.jpe", false, @"C:\a\shot.jpe")]
    [InlineData(@"C:\a\report.foo", false, @"C:\a\report.foo.png")]
    [InlineData(@"C:\a\report.foo", true, @"C:\a\report.foo.jpg")]
    [InlineData(@"C:\a\report", false, @"C:\a\report.png")]
    public void Complete_keeps_supported_extensions_and_appends_the_selected_type(string input, bool jpeg, string expected)
    {
        Assert.Equal(expected, SaveAsPath.Complete(input, jpeg));
    }
}

/// <summary>一時ファイル経由の書き込み（仕様書 §12.3。V1.2.0）。</summary>
public sealed class SafeFileWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "AreaShotTests-" + Guid.NewGuid().ToString("N"));

    public SafeFileWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Action<Stream> Bytes(params byte[] data) => s => s.Write(data);

    /// <summary>途中まで書いてから失敗する（容量不足などの代わり）。</summary>
    private static void WriteThenFail(Stream s)
    {
        s.Write([9, 9, 9]);
        throw new IOException("disk full (test)");
    }

    private string[] Files() => Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void WriteReplacing_creates_a_new_file()
    {
        var path = Path.Combine(_dir, "a.png");
        SafeFileWriter.WriteReplacing(path, Bytes(1, 2, 3));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
        Assert.Equal(["a.png"], Files());
    }

    [Fact]
    public void WriteReplacing_replaces_an_existing_file()
    {
        var path = Path.Combine(_dir, "a.png");
        File.WriteAllBytes(path, [7, 7, 7, 7, 7]);
        SafeFileWriter.WriteReplacing(path, Bytes(1, 2));
        Assert.Equal([1, 2], File.ReadAllBytes(path));
        Assert.Equal(["a.png"], Files());
    }

    [Fact]
    public void WriteReplacing_failure_keeps_the_existing_file_and_leaves_no_temp_file()
    {
        var path = Path.Combine(_dir, "a.png");
        File.WriteAllBytes(path, [7, 7, 7]);
        Assert.Throws<IOException>(() => SafeFileWriter.WriteReplacing(path, WriteThenFail));
        Assert.Equal([7, 7, 7], File.ReadAllBytes(path));
        Assert.Equal(["a.png"], Files());
    }

    [Fact]
    public void WriteReplacing_failure_without_existing_file_leaves_nothing()
    {
        Assert.Throws<IOException>(() => SafeFileWriter.WriteReplacing(Path.Combine(_dir, "a.png"), WriteThenFail));
        Assert.Empty(Files());
    }

    [Fact]
    public void WriteReplacing_read_only_destination_fails_and_keeps_it()
    {
        var path = Path.Combine(_dir, "a.png");
        File.WriteAllBytes(path, [7]);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.ThrowsAny<Exception>(() => SafeFileWriter.WriteReplacing(path, Bytes(1)));
            Assert.Equal([7], File.ReadAllBytes(path));
            Assert.Equal(["a.png"], Files());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void WriteNew_uses_the_chosen_name()
    {
        var path = SafeFileWriter.WriteNew(_dir, () => UniqueFileName.Resolve(_dir, "shot", ".png", File.Exists), Bytes(1));
        Assert.Equal(Path.Combine(_dir, "shot.png"), path);
        Assert.Equal(["shot.png"], Files());
    }

    [Fact]
    public void WriteNew_never_overwrites_and_rechooses_when_the_name_is_taken_before_the_move()
    {
        var first = Path.Combine(_dir, "shot.png");
        File.WriteAllBytes(first, [7]);
        var calls = 0;
        // 1 回目は既存の名前を返す（判定から移すまでの間に別の保存が同じ名前を作った場合と同じ）。2 回目は空いている名前を選ぶ。
        var path = SafeFileWriter.WriteNew(_dir,
            () => ++calls == 1 ? first : UniqueFileName.Resolve(_dir, "shot", ".png", File.Exists), Bytes(1, 2));
        Assert.Equal(2, calls);
        Assert.Equal(Path.Combine(_dir, "shot_2.png"), path);
        Assert.Equal([7], File.ReadAllBytes(first));
        Assert.Equal([1, 2], File.ReadAllBytes(path));
        Assert.Equal(["shot.png", "shot_2.png"], Files());
    }

    [Fact]
    public void WriteNew_write_failure_is_not_treated_as_a_name_conflict()
    {
        var calls = 0;
        Assert.Throws<IOException>(() => SafeFileWriter.WriteNew(_dir, () => { calls++; return Path.Combine(_dir, "shot.png"); }, WriteThenFail));
        Assert.Equal(0, calls); // 書き込みに失敗したら名前を選ばない（連番のファイルを作らない）。
        Assert.Empty(Files());
    }

    [Fact]
    public void WriteNew_gives_up_after_repeated_conflicts_and_leaves_no_temp_file()
    {
        var taken = Path.Combine(_dir, "shot.png");
        File.WriteAllBytes(taken, [7]);
        Assert.Throws<IOException>(() => SafeFileWriter.WriteNew(_dir, () => taken, Bytes(1), maxAttempts: 3));
        Assert.Equal([7], File.ReadAllBytes(taken));
        Assert.Equal(["shot.png"], Files());
    }
}
