using System.Runtime.ExceptionServices;
using System.Windows.Media.Imaging;
using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;
using AreaShot.Core.Imaging;
using AreaShot.Core.Settings;
using AreaShot.Imaging;

namespace AreaShot.Tests;

/// <summary>合成・出力の画素の検証（WPF の描画を使うため STA スレッドで実行する）。</summary>
public class ImagingTests
{
    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    /// <summary>画素ごとに違う値を持つ画像（ずれがあれば必ず検出できる）。</summary>
    private static PixelBuffer Noise(int w, int h)
    {
        var b = PixelBuffer.Create(w, h);
        var rng = new Random(1234);
        rng.NextBytes(b.Pixels);
        for (var i = 3; i < b.Pixels.Length; i += 4) b.Pixels[i] = 255;
        return b;
    }

    [Fact]
    public void Compose_without_annotations_returns_original_pixels()
    {
        RunSta(() =>
        {
            var src = Noise(123, 77);
            var bmp = BitmapConvert.ToBitmapSource(src);
            var result = ImageCompositor.Compose(bmp, [], new MosaicCache(src));
            Assert.Equal(src.Pixels, BitmapConvert.ToPixelBuffer(result).Pixels);
        });
    }

    [Fact]
    public void Compose_keeps_size_and_leaves_untouched_pixels_exact()
    {
        RunSta(() =>
        {
            var src = Noise(301, 157);
            var bmp = BitmapConvert.ToBitmapSource(src);
            // 左上だけに四角形を描く。右下の領域は元画像と完全一致するはず（位置ずれ・補間が無い）。
            var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(10, 10, 40, 30), EditorPresets.DefaultColor, 3);
            var result = BitmapConvert.ToPixelBuffer(ImageCompositor.Compose(bmp, [rect], new MosaicCache(src)));
            Assert.Equal((301, 157), (result.Width, result.Height));
            for (var y = 60; y < 157; y++)
                for (var x = 80; x < 301; x++)
                    Assert.Equal(src.GetArgb(x, y), result.GetArgb(x, y));
            // 枠線の上は色が変わっている。
            var red = EditorPresets.DefaultColor;
            Assert.Equal(0xFF000000u | (uint)red.R << 16 | (uint)red.G << 8 | red.B, result.GetArgb(30, 10));
        });
    }

    [Fact]
    public void Composed_mosaic_matches_core_pixelate_exactly()
    {
        RunSta(() =>
        {
            var src = Noise(200, 120);
            var bmp = BitmapConvert.ToBitmapSource(src);
            var bounds = new RectD(33, 21, 97, 58);
            var mosaic = new MosaicAnnotation(Guid.NewGuid(), bounds);
            var result = BitmapConvert.ToPixelBuffer(ImageCompositor.Compose(bmp, [mosaic], new MosaicCache(src)));
            var region = Mosaic.ToPixelRect(bounds, 200, 120);
            var expected = Mosaic.Pixelate(src, region);
            for (var y = 0; y < region.Height; y++)
                for (var x = 0; x < region.Width; x++)
                    Assert.Equal(expected.GetArgb(x, y), result.GetArgb(region.X + x, region.Y + y));
            // 範囲外は元のまま。
            Assert.Equal(src.GetArgb(region.X - 1, region.Y), result.GetArgb(region.X - 1, region.Y));
            Assert.Equal(src.GetArgb(region.Right, region.Bottom), result.GetArgb(region.Right, region.Bottom));
        });
    }

    [Fact]
    public void Mosaic_source_is_loaded_only_for_first_mosaic()
    {
        RunSta(() =>
        {
            var src = Noise(80, 60);
            var loads = 0;
            var cache = new MosaicCache(() => { loads++; return src; }, src.Width, src.Height);
            var bmp = BitmapConvert.ToBitmapSource(src);
            var rectangle = new RectangleAnnotation(Guid.NewGuid(), new RectD(1, 1, 10, 10), EditorPresets.DefaultColor, 3);
            ImageCompositor.Compose(bmp, [rectangle], cache);
            Assert.Equal(0, loads);

            var id = Guid.NewGuid();
            var region = new RectD(5, 7, 20, 16);
            Assert.NotNull(cache.Get(id, region));
            Assert.NotNull(cache.Get(id, region));
            Assert.Equal(1, loads);
        });
    }

    [Fact]
    public void Composed_line_has_no_arrow_head()
    {
        RunSta(() =>
        {
            var src = Noise(120, 60);
            var bmp = BitmapConvert.ToBitmapSource(src);
            var black = EditorPresets.Colors[1].Color;
            var line = new LineAnnotation(Guid.NewGuid(), new PointD(10, 30), new PointD(110, 30), black, 3);
            var result = BitmapConvert.ToPixelBuffer(ImageCompositor.Compose(bmp, [line], new MosaicCache(src)));
            Assert.Equal(0xFF000000u, result.GetArgb(60, 30));
            Assert.Equal(0xFF000000u, result.GetArgb(108, 30));
            // 矢印なら終点の手前で上下に広がる三角形が描かれる。直線では線幅の外は元画像のまま。
            Assert.Equal(src.GetArgb(105, 24), result.GetArgb(105, 24));
            Assert.Equal(src.GetArgb(105, 36), result.GetArgb(105, 36));
        });
    }

    [Fact]
    public void Mosaic_is_drawn_below_other_annotations()
    {
        RunSta(() =>
        {
            var src = Noise(100, 100);
            var bmp = BitmapConvert.ToBitmapSource(src);
            var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(20, 20, 60, 60), EditorPresets.Colors[1].Color, 5);
            var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 100));
            // モザイクを後から追加しても、四角形（黒）は残る。
            var result = BitmapConvert.ToPixelBuffer(ImageCompositor.Compose(bmp, [rect, mosaic], new MosaicCache(src)));
            Assert.Equal(0xFF000000u, result.GetArgb(50, 20));
        });
    }

    [Fact]
    public void Crop_gives_exact_region_and_opaque_alpha()
    {
        RunSta(() =>
        {
            var src = Noise(50, 40);
            for (var i = 3; i < src.Pixels.Length; i += 4) src.Pixels[i] = 0; // 画面の画素のアルファは不定
            var bmp = BitmapSource.Create(50, 40, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null, src.Pixels, src.Stride);
            var crop = BitmapConvert.CropOpaque(bmp, new System.Windows.Int32Rect(7, 5, 20, 10));
            Assert.Equal((20, 10), (crop.Width, crop.Height));
            var independent = BitmapConvert.CropOpaqueSource(bmp, new System.Windows.Int32Rect(7, 5, 20, 10));
            Assert.Equal(crop.Pixels, BitmapConvert.ToPixelBuffer(independent).Pixels);
            for (var y = 0; y < 10; y++)
                for (var x = 0; x < 20; x++)
                    Assert.Equal(src.GetArgb(7 + x, 5 + y) | 0xFF000000u, crop.GetArgb(x, y));
        });
    }

    [Theory]
    [InlineData(ImageFormatKind.Png)]
    [InlineData(ImageFormatKind.Jpeg)]
    public void Encoded_image_keeps_pixel_size(ImageFormatKind format)
    {
        RunSta(() =>
        {
            var src = Noise(1920, 1080);
            var bmp = BitmapConvert.ToBitmapSource(src);
            using var ms = new MemoryStream();
            ImageEncoder.Encode(bmp, format, 90, ms);
            ms.Position = 0;
            var decoded = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal((1920, 1080), (decoded.PixelWidth, decoded.PixelHeight));
            if (format == ImageFormatKind.Png)
            {
                // PNG は可逆。画素が完全一致する。
                Assert.Equal(src.Pixels, BitmapConvert.ToPixelBuffer(decoded).Pixels);
            }
        });
    }

    [Fact]
    public void Jpeg_quality_changes_file_size()
    {
        RunSta(() =>
        {
            var bmp = BitmapConvert.ToBitmapSource(MosaicTests.Stripes(400, 300));
            long Size(int q)
            {
                using var ms = new MemoryStream();
                ImageEncoder.Encode(bmp, ImageFormatKind.Jpeg, q, ms);
                return ms.Length;
            }
            Assert.True(Size(30) < Size(95));
        });
    }

    [Theory]
    [InlineData(@"C:\a\b.jpg", ImageFormatKind.Jpeg)]
    [InlineData(@"C:\a\b.JPEG", ImageFormatKind.Jpeg)]
    [InlineData(@"C:\a\b.png", ImageFormatKind.Png)]
    public void Format_from_extension(string path, ImageFormatKind expected)
    {
        Assert.Equal(expected, ImageEncoder.FormatFromExtension(path));
    }

    [Fact]
    public void Missing_font_falls_back_without_error()
    {
        RunSta(() =>
        {
            var resolved = FontCatalog.Resolve("存在しないフォント名 12345");
            Assert.False(string.IsNullOrWhiteSpace(resolved));
            Assert.NotEqual("存在しないフォント名 12345", resolved);
            var text = new TextAnnotation(Guid.NewGuid(), new PointD(0, 0), "テスト", EditorPresets.DefaultColor, "存在しないフォント名 12345", 20);
            var size = TextLayout.Measure(text);
            Assert.True(size.Width > 10 && size.Height > 10);
        });
    }
}
