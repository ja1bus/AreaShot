using AreaShot.Core.Licensing;
using AreaShot.Core.Localization;

namespace AreaShot.Tests;

/// <summary>同梱のライセンス表記（仕様書 §14.6。V1.2.0）。</summary>
public class LicenseTests
{
    [Fact]
    public void License_and_notices_are_copied_next_to_the_executable()
    {
        var license = LicenseFiles.Read(LicenseFiles.LicenseName, out var problem);
        Assert.True(license is not null, problem);
        Assert.StartsWith("MIT License", license);
        Assert.Contains("Copyright (c) 2026 ja1bus", license);

        var notices = LicenseFiles.Read(LicenseFiles.NoticesName, out problem);
        Assert.True(notices is not null, problem);
        // MSIX は .NET のランタイムを内包するため、その著作権・許諾表示を含める。
        Assert.Contains("Copyright (c) .NET Foundation and Contributors", notices);

        // 拡張子の無い LICENSE（暗号化される拡張子を避ける）。
        Assert.Equal("", Path.GetExtension(LicenseFiles.PathOf(LicenseFiles.LicenseName)));
        Assert.Equal("https://github.com/ja1bus/AreaShot", AppLinks.Repository);
    }

    [Fact]
    public void Unreadable_license_reports_the_full_path()
    {
        Assert.Null(LicenseFiles.Read("no-such-file", out var problem));
        Assert.Contains(LicenseFiles.PathOf("no-such-file"), problem);
    }
}
