using AreaShot.Core.Geometry;

namespace AreaShot.Core.Imaging;

/// <summary>
/// 32bpp BGRA（上の行から、1 行 = Width × 4 バイト、詰め物なし）の画素。
/// キャプチャー画像の元データとして保持し、モザイク生成に使う。
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "The image size is zero.");
        if (pixels.Length != width * height * 4) throw new ArgumentException("The pixel count does not match the image size.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride => Width * 4;

    /// <summary>BGRA の画素配列。</summary>
    public byte[] Pixels { get; }

    public PixelRect Bounds => new(0, 0, Width, Height);

    public static PixelBuffer Create(int width, int height) => new(width, height, new byte[width * height * 4]);

    /// <summary>(x, y) の画素を 0xAARRGGBB で返す（テスト・確認用）。</summary>
    public uint GetArgb(int x, int y)
    {
        var i = y * Stride + x * 4;
        return (uint)(Pixels[i + 3] << 24 | Pixels[i + 2] << 16 | Pixels[i + 1] << 8 | Pixels[i]);
    }

    public void SetArgb(int x, int y, uint argb)
    {
        var i = y * Stride + x * 4;
        Pixels[i] = (byte)argb;
        Pixels[i + 1] = (byte)(argb >> 8);
        Pixels[i + 2] = (byte)(argb >> 16);
        Pixels[i + 3] = (byte)(argb >> 24);
    }
}
