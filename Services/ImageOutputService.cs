using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AreaShot.Core.Naming;
using AreaShot.Core.Settings;
using AreaShot.Imaging;
using Microsoft.Win32;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

/// <summary>保存ダイアログで確定した出力先と、その時点の画質設定。</summary>
public sealed record SaveAsTarget(string Path, ImageFormatKind Format, int JpegQuality);

/// <summary>
/// 画像の出力（クリップボード・保存先に保存・名前を付けて保存）。キャプチャー後ダイアログとエディタの両方から使う（仕様書 §12）。
/// 失敗した場合はメッセージを表示して false / null を返す。呼び出し側は画面を閉じずに残す（画像を失わないため）。
/// </summary>
public sealed class ImageOutputService
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _notifySaved;
    private string? _lastSaveAsDirectory;

    public ImageOutputService(Func<AppSettings> settings, Action<string> notifySaved)
    {
        _settings = settings;
        _notifySaved = notifySaved;
    }

    public bool CopyToClipboard(BitmapSource image, Window? owner)
    {
        var error = ClipboardService.CopyImage(image);
        if (error is null) return true;
        ShowError(owner, error);
        return false;
    }

    /// <summary>設定の保存先・ファイル名・形式で保存する。保存したパスを返す。</summary>
    public string? SaveToDefault(BitmapSource image, Window? owner)
    {
        var s = _settings();
        var folder = s.ResolveSaveFolder();
        var extension = AppSettings.ExtensionOf(s.ImageFormat);
        MemoryStream encoded;
        try
        {
            encoded = Encode(image, s.ImageFormat, s.JpegQuality);
        }
        catch (Exception ex) when (IsEncodeError(ex))
        {
            ErrorLog.Write("Image encoding failed", ex);
            ShowError(owner, Strings.OutputImageFailed);
            return null;
        }

        using var encodedImage = encoded;
        try
        {
            Directory.CreateDirectory(folder); // 保存先が無ければ作る
            var baseName = FileNameTemplate.Format(s.FileNameTemplate, DateTime.Now);
            // 名前の決定と作成の間に同名ファイルが作られた場合に備え、CreateNew で作り、重なったら取り直す。
            for (var attempt = 0; ; attempt++)
            {
                var path = UniqueFileName.Resolve(folder, baseName, extension, File.Exists);
                try
                {
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        encodedImage.WriteTo(stream);
                    _notifySaved(path);
                    return path;
                }
                catch (IOException) when (File.Exists(path) && attempt < 5)
                {
                    // 同名ファイルが直前に作られた。次の連番で作り直す。
                }
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ErrorLog.Write($"Save failed: {folder}", ex);
            ShowError(owner, string.Format(Strings.OutputSaveFailedFormat, Describe(ex), folder));
            return null;
        }
    }

    /// <summary>Windows 標準の保存ダイアログで保存する。キャンセルまたは失敗時は null。</summary>
    public string? SaveAs(BitmapSource image, Window? owner)
    {
        var target = ChooseSaveAs(owner);
        return target is null ? null : SaveAsToTarget(image, target, owner);
    }

    /// <summary>保存先だけを先に選ぶ。エディタは確定後に画像を合成できる。</summary>
    public SaveAsTarget? ChooseSaveAs(Window? owner)
    {
        var s = _settings();
        var dialog = new SaveFileDialog
        {
            Title = Strings.SaveAsTitle,
            Filter = Strings.SaveAsFilter,
            FilterIndex = s.ImageFormat == ImageFormatKind.Jpeg ? 2 : 1,
            DefaultExt = AppSettings.ExtensionOf(s.ImageFormat),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = FileNameTemplate.Format(s.FileNameTemplate, DateTime.Now),
        };
        var initial = _lastSaveAsDirectory ?? s.ResolveSaveFolder();
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;

        if (dialog.ShowDialog(owner) != true) return null;

        var path = dialog.FileName;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".jpe"))
        {
            // 未対応の拡張子が入力された場合は、選んだ種類の拡張子を付け足す。
            path += dialog.FilterIndex == 2 ? ".jpg" : ".png";
        }
        return new SaveAsTarget(path, ImageEncoder.FormatFromExtension(path), s.JpegQuality);
    }

    /// <summary>確定済みの保存先へ画像を書き込む。</summary>
    public string? SaveAsToTarget(BitmapSource image, SaveAsTarget target, Window? owner)
    {
        var path = target.Path;
        try
        {
            using var encoded = Encode(image, target.Format, target.JpegQuality);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                encoded.WriteTo(stream);
            _lastSaveAsDirectory = Path.GetDirectoryName(path);
            _notifySaved(path);
            return path;
        }
        catch (Exception ex) when (IsFileError(ex) || IsEncodeError(ex))
        {
            ErrorLog.Write($"Save as failed: {path}", ex);
            ShowError(owner, string.Format(Strings.OutputSaveAsFailedFormat, Describe(ex), path));
            return null;
        }
    }

    private static MemoryStream Encode(BitmapSource image, ImageFormatKind format, int quality)
    {
        var ms = new MemoryStream();
        try
        {
            ImageEncoder.Encode(image, format, quality, ms);
            return ms;
        }
        catch
        {
            ms.Dispose();
            throw;
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException;

    private static bool IsEncodeError(Exception ex) =>
        ex is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException or OutOfMemoryException;

    private static string Describe(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Strings.ErrorNoPermission,
        DirectoryNotFoundException => Strings.ErrorFolderNotFound,
        PathTooLongException => Strings.ErrorPathTooLong,
        NotSupportedException or ArgumentException => Strings.ErrorPathInvalid,
        IOException io => io.Message,
        _ => ex.Message,
    };

    private static void ShowError(Window? owner, string message)
    {
        if (owner is not null && owner.IsVisible)
            MessageBox.Show(owner, message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
