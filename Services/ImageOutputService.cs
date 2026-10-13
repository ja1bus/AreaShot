using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using AreaShot.Core.Naming;
using AreaShot.Core.Output;
using AreaShot.Core.Settings;
using AreaShot.Imaging;
using Microsoft.Win32;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

/// <summary>保存ダイアログで確定した出力先と、その時点の画質設定。</summary>
public sealed record SaveAsTarget(string Path, ImageFormatKind Format, int JpegQuality);

/// <summary>
/// 「保存先に保存」の保存対象（V1.3.0）。保存操作の時点の設定から決め、説明入力画面を開いた後に設定が変わっても変えない。
/// </summary>
public sealed record SavePlan(FileNamingMode Mode, string Folder, string Template, ImageFormatKind Format, int JpegQuality)
{
    public string Extension => AppSettings.ExtensionOf(Format);
}

/// <summary>
/// 画像の出力（クリップボード・保存先に保存・名前を付けて保存）。キャプチャー後ダイアログとエディタの両方から使う（仕様書 §12）。
/// 失敗した場合はメッセージを表示して false / null を返す。呼び出し側は画面を閉じずに残す（画像を失わないため）。
/// 作業用フォルダの記憶は AppHost が持つ（「名前を付けて保存」の成功後に AppHost が更新する）。
/// </summary>
public sealed class ImageOutputService
{
    private readonly Func<AppSettings> _settings;
    private readonly Action<string> _notifySaved;

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

    /// <summary>今の設定から「保存先に保存」の保存対象を決める。</summary>
    public SavePlan PlanSave()
    {
        var s = _settings();
        return new SavePlan(s.NamingMode, s.ResolveTargetFolder(), s.FileNameTemplate, s.ImageFormat, s.JpegQuality);
    }

    /// <summary>
    /// 連番の次の名前の見込み（説明入力画面の表示用）。フォルダを読めない場合は <see cref="SequentialName.Error"/> に理由を入れる。
    /// </summary>
    public static SequentialName PreviewSequential(SavePlan plan, string? description)
    {
        try
        {
            return SequentialFileName.Preview(plan.Folder, description, plan.Extension);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ErrorLog.Write($"Numbering failed: {plan.Folder}", ex);
            return new SequentialName(0, "", string.Format(Strings.SequentialNumberingFailedFormat, Describe(ex), plan.Folder));
        }
    }

    /// <summary>今の設定の保存先・名前の付け方・形式で保存する（説明は付けない）。保存したパスを返す。</summary>
    public string? SaveToDefault(BitmapSource image, Window? owner) => Save(image, PlanSave(), null, owner);

    /// <summary>
    /// <paramref name="plan"/> の保存先・名前の付け方で保存する。保存したパスを返す。
    /// 連番は書き込みの直前に採番し、移す間に同名ファイルができた場合は次の番号で移し直す（上書きしない）。
    /// </summary>
    public string? Save(BitmapSource image, SavePlan plan, string? description, Window? owner)
    {
        var folder = plan.Folder;
        MemoryStream encoded;
        try
        {
            encoded = Encode(image, plan.Format, plan.JpegQuality);
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
            Func<string> choosePath;
            if (plan.Mode == FileNamingMode.DateTemplate)
            {
                var baseName = FileNameTemplate.Format(plan.Template, DateTime.Now);
                choosePath = () => UniqueFileName.Resolve(folder, baseName, plan.Extension, File.Exists);
            }
            else
            {
                // 採番できない・長すぎる場合は書く前に止める（一時ファイルも作らない）。
                ChooseSequentialPath(folder, description, plan.Extension);
                choosePath = () => ChooseSequentialPath(folder, description, plan.Extension);
            }
            Directory.CreateDirectory(folder); // 保存先が無ければ作る
            // 一時ファイルに書き終えてから、上書きせずに移す。移す前に同名ファイルが作られた場合だけ次の名前を取り直す（書き込みの失敗は競合として扱わない）。
            var path = SafeFileWriter.WriteNew(folder, choosePath, encodedImage.WriteTo);
            _notifySaved(path);
            return path;
        }
        catch (SequentialNameException ex)
        {
            ShowError(owner, ex.Message);
            return null;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ErrorLog.Write($"Save failed: {folder}", ex);
            ShowError(owner, string.Format(Strings.OutputSaveFailedFormat, Describe(ex), folder));
            return null;
        }
    }

    /// <summary>次の連番のパス。フォルダを読めない・名前が長すぎる場合は <see cref="SequentialNameException"/>。</summary>
    private static string ChooseSequentialPath(string folder, string? description, string extension)
    {
        SequentialName name;
        try
        {
            name = SequentialFileName.Preview(folder, description, extension);
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            ErrorLog.Write($"Numbering failed: {folder}", ex);
            throw new SequentialNameException(string.Format(Strings.SequentialNumberingFailedFormat, Describe(ex), folder));
        }
        if (name.Error is not null) throw new SequentialNameException(name.Error);
        return Path.Combine(folder, name.FileName);
    }

    /// <summary>採番の失敗（メッセージは表示用）。</summary>
    private sealed class SequentialNameException(string message) : Exception(message);

    /// <summary>Windows 標準の保存ダイアログで保存する。キャンセルまたは失敗時は null。</summary>
    public string? SaveAs(BitmapSource image, Window? owner)
    {
        var target = ChooseSaveAs(owner);
        return target is null ? null : SaveAsToTarget(image, target, owner);
    }

    /// <summary>
    /// 保存先だけを先に選ぶ。エディタは確定後に画像を合成できる。
    /// 初期フォルダは作業用フォルダ（無ければ保存フォルダ）、初期名は選んでいる名前の付け方で作る（V1.3.0）。
    /// 連番の候補は初期フォルダで求め、ダイアログでフォルダを変えても振り直さない。説明の入力画面は出さない。
    /// </summary>
    public SaveAsTarget? ChooseSaveAs(Window? owner)
    {
        var s = _settings();
        var initial = s.ResolveWorkFolder();
        if (!Directory.Exists(initial)) initial = s.ResolveSaveFolder();
        var dialog = new SaveFileDialog
        {
            Title = Strings.SaveAsTitle,
            Filter = Strings.SaveAsFilter,
            FilterIndex = s.ImageFormat == ImageFormatKind.Jpeg ? 2 : 1,
            DefaultExt = AppSettings.ExtensionOf(s.ImageFormat),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = InitialSaveAsName(s, initial),
        };
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;

        while (true)
        {
            if (dialog.ShowDialog(owner) != true) return null;

            // 未対応の拡張子が入力された場合は、選んだ種類の拡張子を付け足す。
            var path = SaveAsPath.Complete(dialog.FileName, jpegSelected: dialog.FilterIndex == 2);
            // 付け足したパスはダイアログが上書きを確認していないため、ここで確認する（V1.2.0）。断ったらダイアログに戻る。
            if (path != dialog.FileName && File.Exists(path) && !ConfirmOverwrite(owner, path))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(path);
                dialog.FileName = Path.GetFileName(path);
                continue;
            }
            return new SaveAsTarget(path, ImageEncoder.FormatFromExtension(path), s.JpegQuality);
        }
    }

    private static string InitialSaveAsName(AppSettings s, string folder)
    {
        if (s.NamingMode == FileNamingMode.DateTemplate) return FileNameTemplate.Format(s.FileNameTemplate, DateTime.Now);
        try
        {
            return SequentialFileName.FormatNumber(SequentialFileName.NextNumberIn(folder));
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            // 初期名は利用者が直せるため、採番できなくても保存ダイアログは開く。
            ErrorLog.Write($"Numbering failed: {folder}", ex);
            return SequentialFileName.FormatNumber(1);
        }
    }

    /// <summary>確定済みの保存先へ画像を書き込む。一時ファイルに書き終えてから置き換えるため、失敗しても既存のファイルは残る。</summary>
    public string? SaveAsToTarget(BitmapSource image, SaveAsTarget target, Window? owner)
    {
        var path = target.Path;
        try
        {
            using var encoded = Encode(image, target.Format, target.JpegQuality);
            SafeFileWriter.WriteReplacing(path, encoded.WriteTo);
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

    private static bool ConfirmOverwrite(Window? owner, string path)
    {
        var message = string.Format(Strings.SaveAsOverwriteConfirmFormat, path, Path.GetExtension(path));
        var result = owner is not null && owner.IsVisible
            ? MessageBox.Show(owner, message, Strings.SaveAsTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(message, Strings.SaveAsTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    private static void ShowError(Window? owner, string message)
    {
        if (owner is not null && owner.IsVisible)
            MessageBox.Show(owner, message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
