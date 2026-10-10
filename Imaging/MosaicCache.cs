using System.Windows.Media.Imaging;
using AreaShot.Core.Geometry;
using AreaShot.Core.Imaging;

namespace AreaShot.Imaging;

/// <summary>
/// モザイク画像のキャッシュ（仕様書 §15・性能）。モザイクごと（Id）に「生成した画素範囲」と画像を保持し、
/// 範囲が変わったときだけ、その範囲の分だけ作り直す。画像全体を毎回作り直すことはしない。
/// </summary>
public sealed class MosaicCache
{
    private readonly Func<PixelBuffer> _sourceFactory;
    private readonly int _width;
    private readonly int _height;
    private PixelBuffer? _source;
    private readonly Dictionary<Guid, (PixelRect Rect, BitmapSource Bitmap)> _entries = [];

    public MosaicCache(PixelBuffer source) : this(() => source, source.Width, source.Height)
    {
    }

    public MosaicCache(Func<PixelBuffer> sourceFactory, int width, int height)
    {
        _sourceFactory = sourceFactory;
        _width = width;
        _height = height;
    }

    /// <summary>モザイクの範囲（画像座標）に対応する画像と、描く位置（画素範囲）。範囲が画像の外なら null。</summary>
    public (PixelRect Rect, BitmapSource Bitmap)? Get(Guid id, RectD bounds)
    {
        var rect = Mosaic.ToPixelRect(bounds, _width, _height);
        if (rect.IsEmpty) return null;
        if (_entries.TryGetValue(id, out var entry) && entry.Rect == rect) return entry;
        var bitmap = BitmapConvert.ToBitmapSource(Mosaic.Pixelate(_source ??= _sourceFactory(), rect));
        entry = (rect, bitmap);
        _entries[id] = entry;
        return entry;
    }

    /// <summary>一覧に無くなったモザイクのキャッシュを捨てる。</summary>
    public void Prune(IEnumerable<Guid> alive)
    {
        if (_entries.Count == 0) return;
        var keep = alive as ISet<Guid> ?? alive.ToHashSet();
        foreach (var id in _entries.Keys.Where(k => !keep.Contains(k)).ToList()) _entries.Remove(id);
    }
}
