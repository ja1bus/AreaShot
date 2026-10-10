using AreaShot.Core.Fonts;
using AreaShot.Core.Geometry;
using AreaShot.Core.Imaging;

namespace AreaShot.Tests;

public class MosaicTests
{
    /// <summary>白地に黒の細い縦縞（文字の筆画の代わり）。</summary>
    internal static PixelBuffer Stripes(int w, int h)
    {
        var b = PixelBuffer.Create(w, h);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                b.SetArgb(x, y, x % 3 == 0 ? 0xFF000000u : 0xFFFFFFFFu);
        return b;
    }

    [Fact]
    public void Each_block_is_uniform()
    {
        var src = Stripes(64, 64);
        var region = new PixelRect(8, 8, 40, 40);
        var m = Mosaic.Pixelate(src, region, 16);
        Assert.Equal((40, 40), (m.Width, m.Height));
        for (var by = 0; by < 40; by += 16)
            for (var bx = 0; bx < 40; bx += 16)
            {
                var first = m.GetArgb(bx, by);
                for (var y = by; y < Math.Min(by + 16, 40); y++)
                    for (var x = bx; x < Math.Min(bx + 16, 40); x++)
                        Assert.Equal(first, m.GetArgb(x, y));
            }
    }

    [Fact]
    public void Block_color_is_average_of_source_block()
    {
        var src = PixelBuffer.Create(16, 16);
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                src.SetArgb(x, y, x < 8 ? 0xFF000000u : 0xFFC8C8C8u);
        var m = Mosaic.Pixelate(src, src.Bounds, 16);
        Assert.Equal(0xFF646464u, m.GetArgb(0, 0)); // (0 + 200) / 2 = 100
    }

    [Fact]
    public void Destroys_fine_detail()
    {
        // 縞模様（高周波）は、ブロック内では完全に消える＝読めない。
        var src = Stripes(128, 32);
        var m = Mosaic.Pixelate(src, src.Bounds);
        var distinct = new HashSet<uint>();
        for (var x = 0; x < Mosaic.BlockSize; x++) distinct.Add(m.GetArgb(x, 5));
        Assert.Single(distinct);
        Assert.True(Mosaic.BlockSize >= 12, "文字が判別できないよう、ブロックは十分大きくする");
    }

    [Fact]
    public void Region_is_clipped_to_image()
    {
        Assert.Equal(new PixelRect(90, 0, 10, 20), Mosaic.ToPixelRect(new RectD(90, -10, 50, 30), 100, 100));
        Assert.True(Mosaic.ToPixelRect(new RectD(200, 200, 10, 10), 100, 100).IsEmpty);
    }

    [Fact]
    public void Result_depends_only_on_source_region_not_on_zoom()
    {
        // 表示倍率はモザイクの入力に含まれない（画像座標の範囲だけで決まる）。同じ範囲なら常に同じ結果。
        var src = Stripes(100, 100);
        var a = Mosaic.Pixelate(src, Mosaic.ToPixelRect(new RectD(10.2, 10.4, 50, 50), 100, 100));
        var b = Mosaic.Pixelate(src, Mosaic.ToPixelRect(new RectD(10, 10, 50, 50), 100, 100));
        Assert.Equal(a.Pixels, b.Pixels);
    }

    [Fact]
    public void Source_is_not_modified()
    {
        var src = Stripes(40, 40);
        var copy = (byte[])src.Pixels.Clone();
        Mosaic.Pixelate(src, new PixelRect(0, 0, 40, 40));
        Assert.Equal(copy, src.Pixels);
    }
}

public class FontFallbackTests
{
    [Fact]
    public void Requested_font_is_used_when_installed()
    {
        Assert.Equal("MS Gothic", FontFallback.Resolve("MS Gothic", _ => true, "Segoe UI"));
    }

    [Fact]
    public void Falls_back_to_meiryo_then_yu_gothic_ui_then_system()
    {
        Assert.Equal("Meiryo", FontFallback.Resolve("Nope", n => n == "Meiryo", "Segoe UI"));
        Assert.Equal("Yu Gothic UI", FontFallback.Resolve("Nope", n => n == "Yu Gothic UI", "Segoe UI"));
        Assert.Equal("Segoe UI", FontFallback.Resolve("Nope", _ => false, "Segoe UI"));
        Assert.Equal("Meiryo", FontFallback.Resolve(null, n => n is "Meiryo" or "Yu Gothic UI", "Segoe UI"));
    }

    [Fact]
    public void Exception_in_font_lookup_does_not_crash()
    {
        Assert.Equal("Segoe UI", FontFallback.Resolve("X", _ => throw new InvalidOperationException(), "Segoe UI"));
    }

    [Fact]
    public void Candidates_include_spec_minimum()
    {
        Assert.Contains("Meiryo", FontFallback.Candidates);
        Assert.Contains("Yu Gothic UI", FontFallback.Candidates);
        Assert.Contains("MS Gothic", FontFallback.Candidates);
    }
}
