using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AreaShot.Core.Annotations;
using AreaShot.Core.Geometry;
using AreaShot.Core.Imaging;
using AreaShot.Core.Settings;
using AreaShot.Imaging;

namespace AreaShot.Tests;

/// <summary>V1.4.0 の描画・合成・文字の測定（verification.md T-08・T-26・T-31・T-38・T-51・T-52・T-58）。STA で実行する。</summary>
public class V140ImagingTests
{
    private static readonly ArgbColor Red = ArgbColor.FromRgb(0xE6, 0x00, 0x12);

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

    private static PixelBuffer Solid(int w, int h, byte r, byte g, byte b)
    {
        var buf = PixelBuffer.Create(w, h);
        for (var i = 0; i < buf.Pixels.Length; i += 4)
        {
            buf.Pixels[i] = b;
            buf.Pixels[i + 1] = g;
            buf.Pixels[i + 2] = r;
            buf.Pixels[i + 3] = 255;
        }
        return buf;
    }

    /// <summary>画素 (x, y) の色を (R=x, G=y, B=0) にした画像。</summary>
    private static PixelBuffer Gradient(int w, int h)
    {
        var buf = PixelBuffer.Create(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = y * buf.Stride + x * 4;
            buf.Pixels[i] = 0;
            buf.Pixels[i + 1] = (byte)y;
            buf.Pixels[i + 2] = (byte)x;
            buf.Pixels[i + 3] = 255;
        }
        return buf;
    }

    private static (byte R, byte G, byte B) At(PixelBuffer b, int x, int y)
    {
        var i = y * b.Stride + x * 4;
        return (b.Pixels[i + 2], b.Pixels[i + 1], b.Pixels[i]);
    }

    private static void Near((byte R, byte G, byte B) expected, (byte R, byte G, byte B) actual, int tolerance = 2)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"expected {expected} ±{tolerance}, actual {actual}");
    }

    private static PixelBuffer Compose(PixelBuffer src, params Annotation[] annotations) =>
        BitmapConvert.ToPixelBuffer(ImageCompositor.Compose(BitmapConvert.ToBitmapSource(src), annotations, new MosaicCache(src)));

    [Fact]
    public void T08_badge_is_a_filled_circle_with_a_white_number()
    {
        RunSta(() =>
        {
            var src = Solid(100, 100, 255, 255, 255);
            var outp = Compose(src, new BadgeAnnotation(Guid.NewGuid(), new PointD(50, 50), 1, Red, 20));
            Assert.Equal((100, 100), (outp.Width, outp.Height));
            Near((0xE6, 0x00, 0x12), At(outp, 62, 50));
            Near((0xE6, 0x00, 0x12), At(outp, 50, 63));
            Assert.Equal(At(src, 64, 64), At(outp, 64, 64));
            Assert.Equal(At(src, 70, 50), At(outp, 70, 50));
            Assert.True(HasWhiteNear(outp, 50, 50, 6));

            var ten = Compose(src, new BadgeAnnotation(Guid.NewGuid(), new PointD(50, 50), 10, Red, 20));
            Assert.Equal((100, 100), (ten.Width, ten.Height));
            Assert.True(HasWhiteNear(ten, 50, 50, 6));
        });
    }

    private static bool HasWhiteNear(PixelBuffer b, int cx, int cy, int radius)
    {
        for (var y = cy - radius; y <= cy + radius; y++)
        for (var x = cx - radius; x <= cx + radius; x++)
        {
            if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius) continue;
            var (r, g, bl) = At(b, x, y);
            if (r >= 240 && g >= 240 && bl >= 240) return true;
        }
        return false;
    }

    [Fact]
    public void T26_highlight_uses_a_fixed_alpha_in_the_output()
    {
        RunSta(() =>
        {
            var white = Solid(100, 100, 255, 255, 255);
            var black = Solid(100, 100, 0, 0, 0);
            var h = new HighlightAnnotation(Guid.NewGuid(), new RectD(20, 20, 40, 40), ArgbColor.FromRgb(0xE6, 0x00, 0x12));
            var onWhite = Compose(white, h);
            Near((246, 159, 166), At(onWhite, 40, 40));
            Near((87, 0, 7), At(Compose(black, h), 40, 40));
            Assert.Equal(At(white, 10, 10), At(onWhite, 10, 10));

            var translucentInput = h with { Color = new ArgbColor(0x20, 0xE6, 0x00, 0x12) };
            Assert.Equal(onWhite.Pixels, Compose(white, translucentInput).Pixels);

            var empty = h with { Bounds = new RectD(20, 20, 0, 40) };
            Assert.Equal(white.Pixels, Compose(white, empty).Pixels);

            var rect = new RectangleAnnotation(Guid.NewGuid(), new RectD(30, 30, 20, 20), Red, 3);
            // 描画順（作成順）の確認。赤い線に赤のハイライトを重ねても赤のままで区別できないため、黄色のハイライトで比べる
            //（試験表 T-26 の期待値の前提の誤り。確認記録に記載）。
            var yellow = h with { Color = ArgbColor.FromRgb(0xFF, 0xD7, 0x00) };
            var rectOnTop = Compose(white, yellow, rect);
            var highlightOnTop = Compose(white, rect, yellow);
            Near((0xE6, 0x00, 0x12), At(rectOnTop, 30, 40));
            Assert.True(At(highlightOnTop, 30, 40).G > At(rectOnTop, 30, 40).G + 20, "highlight drawn above the rectangle should lighten the red line");
        });
    }

    [Fact]
    public void T31_text_outline_is_drawn_around_the_text()
    {
        RunSta(() =>
        {
            var white = Solid(200, 100, 255, 255, 255);
            var text = new TextAnnotation(Guid.NewGuid(), new PointD(20, 20), "ABC", ArgbColor.FromRgb(255, 255, 255), "Meiryo", 20);
            // 白地の白文字は、縁取りが無ければ見えない（描画の丸めで 254 になる画素があるため ±2 で比べる。1.3.3 からの挙動）。
            var plain = Compose(white, text);
            for (var i = 0; i < plain.Pixels.Length; i++) Assert.InRange(plain.Pixels[i], 253, 255);

            var outlined = Compose(white, text with { Outline = true });
            var bounds = AnnotationOps.BoundsOf(text, TextLayout.Measurer).Inflate(3);
            var dark = 0;
            for (var y = 0; y < 100; y++)
            for (var x = 0; x < 200; x++)
            {
                var p = At(outlined, x, y);
                var inside = x >= bounds.Left && x < bounds.Right && y >= bounds.Top && y < bounds.Bottom;
                if (!inside) Assert.Equal(At(white, x, y), p);
                else if (p.R <= 64 && p.G <= 64 && p.B <= 64) dark++;
            }
            Assert.True(dark > 0, "the outline should have dark pixels");

            var black = Solid(200, 100, 0, 0, 0);
            var blackText = (TextAnnotation)AnnotationOps.WithStyle(text with { Outline = true }, color: ArgbColor.FromRgb(0, 0, 0));
            var onBlack = Compose(black, blackText);
            var light = 0;
            for (var i = 0; i < onBlack.Pixels.Length; i += 4)
                if (onBlack.Pixels[i] >= 192 && onBlack.Pixels[i + 1] >= 192 && onBlack.Pixels[i + 2] >= 192) light++;
            Assert.True(light > 0, "a dark text should get a white outline");
        });
    }

    [Fact]
    public void T38_crop_cuts_the_composed_image()
    {
        RunSta(() =>
        {
            var src = Gradient(100, 80);
            var crop = new CropAnnotation(Guid.NewGuid(), new RectD(10, 20, 30, 40));

            var bmp = ImageCompositor.Compose(BitmapConvert.ToBitmapSource(src), [crop], new MosaicCache(src));
            Assert.True(bmp.IsFrozen);
            var a = BitmapConvert.ToPixelBuffer(bmp);
            Assert.Equal((30, 40), (a.Width, a.Height));
            Assert.Equal(((byte)10, (byte)20, (byte)0), At(a, 0, 0));
            Assert.Equal(((byte)39, (byte)59, (byte)0), At(a, 29, 39));
            for (var y = 0; y < 40; y++)
            for (var x = 0; x < 30; x++)
                Assert.Equal(At(src, x + 10, y + 20), At(a, x, y));

            Assert.Equal(src.Pixels, Compose(src, crop with { Bounds = new RectD(150, 150, 10, 10) }).Pixels);
            Assert.Equal(src.Pixels, Compose(src, crop with { Bounds = new RectD(0, 0, 100, 80) }).Pixels);

            var frame = new RectangleAnnotation(Guid.NewGuid(), new RectD(0, 0, 100, 80), Red, 3);
            var d = Compose(src, frame, crop);
            for (var i = 0; i < d.Pixels.Length; i += 4)
                Assert.False(d.Pixels[i + 2] >= 0xE4 && d.Pixels[i + 1] <= 2, "annotations outside the crop must be cut off");

            var inner = new RectangleAnnotation(Guid.NewGuid(), new RectD(15, 25, 10, 10), Red, 1);
            var e = Compose(src, inner, crop);
            var reddish = false;
            for (var y = 3; y <= 17 && !reddish; y++)
            for (var x = 3; x <= 7 && !reddish; x++)
                reddish = At(e, x, y).R >= 100;
            Assert.True(reddish, "an annotation inside the crop should appear near (5,5)");

            var mosaic = new MosaicAnnotation(Guid.NewGuid(), new RectD(10, 20, 32, 32));
            var f = Compose(src, mosaic, crop);
            var expected = Mosaic.Pixelate(src, new PixelRect(10, 20, 32, 32));
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 30; x++)
                Assert.Equal(At(expected, x, y), At(f, x, y));
        });
    }

    [Fact]
    public void T51_cached_measure_equals_uncached()
    {
        RunSta(() =>
        {
            var id = Guid.NewGuid();
            var t = new TextAnnotation(id, new PointD(1, 2), "ABC", Red, "Meiryo", 20);
            Assert.Equal(TextLayout.MeasureUncached(t), TextLayout.Measure(t));
            Assert.Equal(TextLayout.MeasureUncached(t), TextLayout.Measure(t));
            var other = t with { Id = Guid.NewGuid(), Position = new PointD(50, 60), Color = ArgbColor.FromRgb(0, 0, 255) };
            Assert.Equal(TextLayout.MeasureUncached(other), TextLayout.Measure(other));

            var longer = t with { Text = "ABCDEF" };
            Assert.Equal(TextLayout.MeasureUncached(longer), TextLayout.Measure(longer));
            Assert.True(TextLayout.Measure(longer).Width > TextLayout.Measure(t).Width);

            var bigger = t with { FontSize = 40 };
            Assert.Equal(TextLayout.MeasureUncached(bigger), TextLayout.Measure(bigger));
            Assert.True(TextLayout.Measure(bigger).Height > TextLayout.Measure(t).Height);

            var otherFont = FontCatalog.Available.FirstOrDefault(f => f != "Meiryo") ?? "MS Gothic";
            var i1 = t with { Text = "iiii" };
            var i2 = i1 with { FontFamily = otherFont };
            Assert.Equal(TextLayout.MeasureUncached(i1), TextLayout.Measure(i1));
            Assert.Equal(TextLayout.MeasureUncached(i2), TextLayout.Measure(i2));

            var twoLines = t with { Text = "A\nB" };
            Assert.Equal(TextLayout.MeasureUncached(twoLines), TextLayout.Measure(twoLines));
            Assert.True(TextLayout.Measure(twoLines).Height > TextLayout.Measure(t with { Text = "A" }).Height);
        });
    }

    [Fact]
    public void T52_cache_is_bounded_and_thread_safe()
    {
        RunSta(() =>
        {
            TextAnnotation Make(int i) => new(Guid.NewGuid(), default, "t" + i, Red, "Meiryo", 20);
            for (var i = 0; i < 512; i++)
            {
                Assert.Equal(TextLayout.MeasureUncached(Make(i)), TextLayout.Measure(Make(i)));
                Assert.True(TextLayout.CachedCount <= TextLayout.CacheLimit);
            }
            Assert.Equal(TextLayout.MeasureUncached(Make(512)), TextLayout.Measure(Make(512)));
            Assert.True(TextLayout.CachedCount <= TextLayout.CacheLimit);
            Assert.Equal(TextLayout.MeasureUncached(Make(0)), TextLayout.Measure(Make(0)));
        });

        var reference = new SizeD[600];
        RunSta(() =>
        {
            for (var i = 0; i < 600; i++)
                reference[i] = TextLayout.MeasureUncached(new TextAnnotation(Guid.NewGuid(), default, "t" + i, Red, "Meiryo", 20));
        });
        var errors = new List<string>();
        var threads = Enumerable.Range(0, 8).Select(n => new Thread(() =>
        {
            var rng = new Random(n);
            try
            {
                for (var k = 0; k < 1000; k++)
                {
                    var i = rng.Next(600);
                    var size = TextLayout.Measure(new TextAnnotation(Guid.NewGuid(), default, "t" + i, Red, "Meiryo", 20));
                    if (size != reference[i]) lock (errors) errors.Add($"t{i}: {size} != {reference[i]}");
                }
            }
            catch (Exception ex)
            {
                lock (errors) errors.Add(ex.ToString());
            }
        })).ToList();
        foreach (var t in threads) t.SetApartmentState(ApartmentState.STA);
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        Assert.Empty(errors);
    }

    [Fact]
    public void T58_encoding_keeps_the_composed_pixels()
    {
        RunSta(() =>
        {
            var src = Gradient(100, 80);
            Annotation[] list =
            [
                new RectangleAnnotation(Guid.NewGuid(), new RectD(5, 5, 30, 20), Red, 3),
                new ArrowAnnotation(Guid.NewGuid(), new PointD(10, 70), new PointD(90, 40), Red, 3),
                new TextAnnotation(Guid.NewGuid(), new PointD(40, 10), "Ab", Red, "Meiryo", 16),
                new MosaicAnnotation(Guid.NewGuid(), new RectD(60, 50, 30, 20)),
            ];
            var composed = ImageCompositor.Compose(BitmapConvert.ToBitmapSource(src), list, new MosaicCache(src));
            Assert.Equal((100, 80), (composed.PixelWidth, composed.PixelHeight));

            using var png = new MemoryStream();
            ImageEncoder.Encode(composed, ImageFormatKind.Png, 90, png);
            png.Position = 0;
            var decoded = BitmapDecoder.Create(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(PixelFormats.Bgr24, decoded.Format);
            Assert.Equal(BitmapConvert.ToPixelBuffer(composed).Pixels, BitmapConvert.ToPixelBuffer(decoded).Pixels);

            long JpegSize(int quality)
            {
                using var ms = new MemoryStream();
                ImageEncoder.Encode(composed, ImageFormatKind.Jpeg, quality, ms);
                ms.Position = 0;
                var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                Assert.Equal((100, 80), (frame.PixelWidth, frame.PixelHeight));
                return ms.Length;
            }
            Assert.True(JpegSize(30) < JpegSize(90));
        });
    }
}
