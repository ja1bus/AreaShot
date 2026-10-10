using System.IO;
using System.Windows.Input;

namespace AreaShot.Capture;

/// <summary>
/// 範囲選択用の十字カーソル（仕様書 §5）。黒い線を白い縁で囲み、明るい画面・暗い画面のどちらでも見えるようにする。
/// 標準の Cursors.Cross は細い反転色で、暗幕を掛けた画面では見にくかった（V1.0.2）。
/// 拡大率に合わせた大きさの .cur をその場で作り、拡大・縮小でぼやけないようにする。
/// </summary>
internal static class CrosshairCursor
{
    private const uint Black = 0xFF000000;
    private const uint White = 0xFFFFFFFF;

    /// <param name="scale">表示の拡大率（1.0 = 100%）。</param>
    public static Cursor Create(double scale)
    {
        var unit = Math.Max(1, (int)Math.Round(scale)); // 縁の太さ（物理ピクセル）
        var size = Math.Clamp((int)Math.Round(32 * scale), 32, 255);
        var hot = size / 2;
        var pixels = new uint[size * size]; // 上の行から。0 = 透明

        // 縁（白）を先に塗り、その内側に線（黒）を重ねる。線は位置を合わせやすいよう細め（250% まで 1px）。
        var half = (unit - 1) / 2;
        FillCross(pixels, size, hot, half + unit, 0, size - 1, White);
        FillCross(pixels, size, hot, half, unit, size - 1 - unit, Black);

        using var stream = new MemoryStream(ToCur(pixels, size, hot));
        return new Cursor(stream, scaleWithDpi: false);
    }

    /// <summary>中心 <paramref name="hot"/> を通る縦横の帯（中心から ±<paramref name="half"/>）を from..to の範囲で塗る。</summary>
    private static void FillCross(uint[] pixels, int size, int hot, int half, int from, int to, uint color)
    {
        for (var i = from; i <= to; i++)
        {
            for (var d = -half; d <= half; d++)
            {
                var c = hot + d;
                if (c < 0 || c >= size) continue;
                pixels[c * size + i] = color; // 横線
                pixels[i * size + c] = color; // 縦線
            }
        }
    }

    /// <summary>32bit ARGB 1 枚の .cur ファイルのバイト列。</summary>
    private static byte[] ToCur(uint[] pixels, int size, int hot)
    {
        var maskStride = (size + 31) / 32 * 4;
        var imageBytes = 40 + size * size * 4 + maskStride * size;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // ICONDIR
        w.Write((ushort)0);
        w.Write((ushort)2); // カーソル
        w.Write((ushort)1);
        // ICONDIRENTRY
        w.Write((byte)size);
        w.Write((byte)size);
        w.Write((byte)0);
        w.Write((byte)0);
        w.Write((ushort)hot);
        w.Write((ushort)hot);
        w.Write((uint)imageBytes);
        w.Write((uint)22);
        // BITMAPINFOHEADER（高さは XOR + AND の 2 倍）
        w.Write(40);
        w.Write(size);
        w.Write(size * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(size * size * 4);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        // 色（下の行から、BGRA）
        for (var y = size - 1; y >= 0; y--)
            for (var x = 0; x < size; x++)
                w.Write(pixels[y * size + x]);
        // AND マスク（透明度はアルファで表すため全 0）
        w.Write(new byte[maskStride * size]);
        w.Flush();
        return ms.ToArray();
    }
}
