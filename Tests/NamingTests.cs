using AreaShot.Core.Naming;

namespace AreaShot.Tests;

public class FileNameTemplateTests
{
    private static readonly DateTime Sample = new(2026, 9, 22, 22, 40, 12, 987);

    [Fact]
    public void Default_template_matches_spec_example_without_milliseconds()
    {
        Assert.Equal("260922224012_capture", FileNameTemplate.Format(FileNameTemplate.Default, Sample));
    }

    [Fact]
    public void All_tokens_expand()
    {
        Assert.Equal("2026-26-09-22_22-40-12", FileNameTemplate.Format("{yyyy}-{yy}-{MM}-{dd}_{HH}-{mm}-{ss}", Sample));
    }

    [Fact]
    public void Hours_use_24_hour_clock_and_are_zero_padded()
    {
        Assert.Equal("260101_050709", FileNameTemplate.Format("{yy}{MM}{dd}_{HH}{mm}{ss}", new DateTime(2026, 1, 1, 5, 7, 9)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_template_falls_back_to_default(string? template)
    {
        Assert.Equal("260922224012_capture", FileNameTemplate.Format(template, Sample));
    }

    [Fact]
    public void Invalid_characters_are_replaced_when_formatting()
    {
        Assert.Equal("a_b_c_260922", FileNameTemplate.Format("a:b*c?{yy}{MM}{dd}", Sample));
    }

    [Fact]
    public void Reserved_device_name_gets_suffix()
    {
        Assert.Equal("CON_", FileNameTemplate.Format("CON", Sample));
    }

    [Fact]
    public void Validate_accepts_default_and_plain_text()
    {
        Assert.Null(FileNameTemplate.Validate(FileNameTemplate.Default));
        Assert.Null(FileNameTemplate.Validate("スクリーンショット_{yyyy}{MM}{dd}"));
    }

    [Theory]
    [InlineData("{yy}{fff}", "{fff}")]
    [InlineData("{hour}", "{hour}")]
    public void Validate_rejects_unknown_tokens(string template, string token)
    {
        var error = FileNameTemplate.Validate(template);
        Assert.NotNull(error);
        Assert.Contains(token, error);
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a|b")]
    [InlineData("a<b>")]
    [InlineData("")]
    public void Validate_rejects_invalid_names(string template)
    {
        Assert.NotNull(FileNameTemplate.Validate(template));
    }
}

public class UniqueFileNameTests
{
    [Fact]
    public void Returns_plain_name_when_no_conflict()
    {
        var path = UniqueFileName.Resolve(@"C:\shots", "260922224012_capture", ".png", _ => false);
        Assert.Equal(@"C:\shots\260922224012_capture.png", path);
    }

    [Fact]
    public void Adds_2_then_3_on_conflicts()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\shots\260922224012_capture.png",
            @"C:\shots\260922224012_capture_2.png",
        };
        Assert.Equal(@"C:\shots\260922224012_capture_3.png",
            UniqueFileName.Resolve(@"C:\shots", "260922224012_capture", ".png", existing.Contains));
    }

    [Fact]
    public void First_conflict_uses_suffix_2()
    {
        var existing = new HashSet<string> { @"C:\shots\x.jpg" };
        Assert.Equal(@"C:\shots\x_2.jpg", UniqueFileName.Resolve(@"C:\shots", "x", "jpg", existing.Contains));
    }

    [Fact]
    public void Works_against_real_directory()
    {
        var dir = Directory.CreateTempSubdirectory("areashot-test-").FullName;
        try
        {
            var first = UniqueFileName.Resolve(dir, "shot", ".png", File.Exists);
            File.WriteAllText(first, "");
            var second = UniqueFileName.Resolve(dir, "shot", ".png", File.Exists);
            File.WriteAllText(second, "");
            var third = UniqueFileName.Resolve(dir, "shot", ".png", File.Exists);
            Assert.Equal(new[] { "shot.png", "shot_2.png", "shot_3.png" }, new[] { first, second, third }.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
