using System.Windows.Media.Imaging;
using AreaShot.Core.Geometry;
using AreaShot.Core.Imaging;
using AreaShot.Imaging;

namespace AreaShot.Capture;

/// <summary>
/// 1 回分のキャプチャー画像（元画像）。キャプチャー後ダイアログ → エディタへ受け渡す。
/// <see cref="Pixels"/> はモザイク生成時だけ作る元画素、<see cref="Source"/> は表示・出力用（どちらも変更しない）。
/// </summary>
public sealed class CapturedImage
{
    private PixelBuffer? _pixels;

    public CapturedImage(BitmapSource source, PixelRect screenRect)
    {
        Source = source;
        ScreenRect = screenRect;
    }

    public PixelBuffer Pixels => _pixels ??= BitmapConvert.ToPixelBuffer(Source);
    public BitmapSource Source { get; }

    /// <summary>取得した範囲（仮想デスクトップの物理ピクセル座標）。ダイアログ・エディタを出すモニターの判定に使う。</summary>
    public PixelRect ScreenRect { get; }

    public int Width => Source.PixelWidth;
    public int Height => Source.PixelHeight;

    public (int X, int Y) ScreenCenter => (ScreenRect.X + ScreenRect.Width / 2, ScreenRect.Y + ScreenRect.Height / 2);
}
