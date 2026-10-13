using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AreaShot.Capture;
using AreaShot.Core.Geometry;
using AreaShot.Core.Input;
using AreaShot.Core.Naming;
using AreaShot.Core.Settings;
using AreaShot.Editor;
using AreaShot.Imaging;
using AreaShot.Services;
using AreaShot.Views;
using Microsoft.Win32;
using AreaShot.Core.Localization;

namespace AreaShot;

/// <summary>
/// アプリの構成ルート（仕様書 §2）。タスクトレイ・ショートカット・設定を持ち、
/// キャプチャー → キャプチャー後ダイアログ → エディタ → 出力 の流れをつなぐ。
/// 各画面は互いを直接知らず、操作はイベントでここへ戻ってくる。
/// </summary>
public sealed class AppHost : ISettingsHost, IDisposable
{
    /// <summary>トレイのメニュー・「もう一度キャプチャー」から始めるときの待ち時間（メニューやダイアログが画面から消えるのを待つ）。</summary>
    private const int MenuCloseDelayMs = 250;

    private readonly Application _app;
    /// <summary>起動時の Windows の表示言語（「Windows の表示言語に合わせる」の判定に使う）。</summary>
    private readonly CultureInfo _windowsUiCulture;
    /// <summary>この起動で使っている表示言語（終了まで変えない）。</summary>
    private CultureInfo _uiCulture;
    private readonly SettingsStore _store = SettingsStore.CreateDefault();
    private readonly ImageOutputService _output;
    private AppSettings _settings = new();
    private TrayService? _tray;
    private GlobalHotkeyService? _hotkey;
    private CaptureOverlayWindow? _overlay;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _delayedCapture;
    private bool _memoryCollectionQueued;
    private bool _choosingWorkFolder;
    private bool _disposed;

    public AppHost(Application app, CultureInfo windowsUiCulture)
    {
        _app = app;
        _windowsUiCulture = windowsUiCulture;
        _uiCulture = UiLanguage.Resolve(UiLanguage.System, windowsUiCulture);
        _output = new ImageOutputService(() => _settings, NotifySaved);
    }

    public AppSettings Settings => _settings;

    public HotkeyRegistration HotkeyRegistration => _hotkey?.Current ?? HotkeyRegistration.Unassigned;

    public event EventHandler? HotkeyRegistrationChanged;

    /// <summary>作業用フォルダ（画面では「連番の保存先」）が変わった（設定画面の表示を更新するため。V1.3.0）。</summary>
    public event EventHandler? WorkFolderChanged;

    /// <summary>キャプチャー後ダイアログ・エディタに出す保存先（V1.3.0）。</summary>
    private SaveDestination CurrentDestination()
    {
        var folder = _settings.ResolveTargetFolder();
        return new SaveDestination(_settings.NamingMode, folder, Directory.Exists(folder), SaveTooltip());
    }

    /// <summary>「保存先に保存」のツールチップ。次に保存する名前の例と、ファイル名の付け方。</summary>
    private string SaveTooltip()
    {
        var folder = _settings.ResolveTargetFolder();
        var extension = AppSettings.ExtensionOf(_settings.ImageFormat);
        string example;
        if (_settings.NamingMode == FileNamingMode.DateTemplate)
        {
            example = Path.Combine(folder, FileNameTemplate.Format(_settings.FileNameTemplate, DateTime.Now) + extension);
        }
        else
        {
            var next = ImageOutputService.PreviewSequential(_output.PlanSave(), null);
            example = next.Number > 0 ? Path.Combine(folder, next.FileName) : folder;
        }
        return string.Format(Strings.SaveTooltipFormat, example) + "\n" +
               string.Format(Strings.SaveTooltipModeFormat, FileNamingModes.Describe(_settings.NamingMode));
    }

    public void Start()
    {
        var load = _store.Load();
        _settings = load.Settings;
        foreach (var fix in load.Fixes) ErrorLog.Write("Setting corrected: " + fix);

        // 表示言語は、トレイなどの画面を作る前に決める（以降の画面・メッセージはこの言語で出る）。
        _uiCulture = UiLanguage.Resolve(_settings.Language, _windowsUiCulture);
        UiLanguage.Apply(_uiCulture);

        _tray = new TrayService();
        _tray.CaptureRequested += (_, _) => StartCapture(MenuCloseDelayMs);
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => Exit();

        _hotkey = new GlobalHotkeyService();
        _hotkey.Pressed += (_, _) => StartCapture(0);
        _hotkey.StatusChanged += (_, _) =>
        {
            _tray?.UpdateHotkey(_hotkey.Current);
            HotkeyRegistrationChanged?.Invoke(this, EventArgs.Empty);
        };

        if (load.Status == SettingsLoadStatus.Corrupt)
        {
            ErrorLog.Write($"Could not read the settings file (started with defaults): {load.Error} Backup: {load.BackupPath}");
            _tray.ShowBalloon("AreaShot", Strings.SettingsCorruptBalloon +
                (load.BackupPath is null ? "" : "\n" + string.Format(Strings.SettingsCorruptBackupFormat, Path.GetFileName(load.BackupPath))), warning: true);
        }

        var registration = RegisterHotkeyFromSettings();
        if (registration.Status == HotkeyStatus.Failed)
        {
            _tray.ShowBalloon(Strings.HotkeyFailedBalloonTitle,
                registration.Describe() + "\n" + Strings.HotkeyFailedBalloonHint, warning: true);
        }
        else if (load.Status == SettingsLoadStatus.Missing)
        {
            _tray.ShowBalloon("AreaShot", string.Format(Strings.StartedBalloonFormat, registration.Shortcut), warning: false);
        }

        AutoStartService.RefreshPathIfMissing();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private HotkeyRegistration RegisterHotkeyFromSettings()
    {
        Shortcut.TryParse(_settings.CaptureHotkey, out var shortcut);
        return _hotkey!.Apply(shortcut);
    }

    // ---- キャプチャー ----

    /// <summary>キャプチャーを始める。<paramref name="delayMs"/> 後に画面を取得する（0 なら即時）。</summary>
    public void StartCapture(int delayMs)
    {
        if (_disposed) return;
        // BeginCapture は呼ぶ前（JIT 時）に画像処理の DLL を読み込むため、exe の有無はここで確かめる。
        if (!App.EnsureExecutablePresent()) return;
        if (_overlay is not null)
        {
            _overlay.BringToFront(); // 範囲選択中にもう一度押された
            return;
        }
        if (_delayedCapture is not null) return;
        if (delayMs <= 0)
        {
            BeginCapture();
            return;
        }
        _delayedCapture = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        _delayedCapture.Tick += (_, _) =>
        {
            _delayedCapture?.Stop();
            _delayedCapture = null;
            if (!App.EnsureExecutablePresent()) return;
            BeginCapture();
        };
        _delayedCapture.Start();
    }

    private void BeginCapture()
    {
        ToastWindow.CloseCurrent(); // 通知がキャプチャーに写らないように
        var snapshot = ScreenCapturer.CaptureVirtualScreen();
        if (snapshot is null)
        {
            _tray?.ShowBalloon("AreaShot", Strings.CaptureScreenFailed, warning: true);
            return;
        }

        var overlay = new CaptureOverlayWindow(snapshot, _settings.QuickActions);
        overlay.Selected += selection => OnAreaSelected(snapshot, selection);
        overlay.Cancelled += ReleaseMemorySoon;
        overlay.Closed += (_, _) =>
        {
            if (_overlay == overlay) _overlay = null;
        };
        _overlay = overlay;
        overlay.Show();

        // エディタで使うフォント一覧を、範囲を選んでいる間に読み込んでおく。
        FontCatalog.WarmUpOnceInBackground();
    }

    private void OnAreaSelected(ScreenSnapshot snapshot, AreaSelection selection)
    {
        var local = SelectionMath.ToImageSpace(selection.Rect, snapshot.Bounds);
        var source = BitmapConvert.CropOpaqueSource(snapshot.Image, new Int32Rect(local.X, local.Y, local.Width, local.Height));
        var image = new CapturedImage(source, selection.Rect);
        if (!RunQuickAction(image, selection.Action))
            ShowResultDialog(image, selection.ReleaseX, selection.ReleaseY);
        // 選択が終われば仮想画面全体の静止画は不要。ダイアログ・エディタが開いていても回収候補にする。
        ReleaseMemorySoon();
    }

    /// <summary>
    /// キーを押しながら離したときの操作をダイアログなしで実行する（仕様書 §5.4）。
    /// 実行しなかった・失敗した・名前を付けて保存をキャンセルした場合は false（画像を失わないようダイアログを出す）。
    /// </summary>
    private bool RunQuickAction(CapturedImage image, QuickAction? action)
    {
        var done = action switch
        {
            QuickAction.Copy => _output.CopyToClipboard(image.Source, null),
            // 連番＋説明では説明の入力画面を出す。キャンセルしたらダイアログを出す（画像を失わない）。
            QuickAction.Save => SaveToDestination(_ => image.Source, null),
            QuickAction.SaveAs => SaveAs(image.Source, null),
            QuickAction.Edit => true,
            _ => false,
        };
        if (!done) return false;
        if (action == QuickAction.Edit)
        {
            OpenEditor(image);
            return true;
        }
        if (action == QuickAction.Copy && _settings.ShowSaveNotification) ToastWindow.ShowCopied();
        ReleaseMemorySoon();
        return true;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // 別スレッドから来ることがある。範囲選択中にモニター構成が変わったら、座標が合わなくなるので中止する。
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (_overlay is null) return;
            _overlay.Cancel();
            _tray?.ShowBalloon("AreaShot", Strings.CaptureCancelledDisplayChanged, warning: false);
        });
    }

    // ---- キャプチャー後ダイアログ ----

    /// <summary>ダイアログを、コピーボタンがマウスを離した点（物理ピクセル）に来るように出す。</summary>
    private void ShowResultDialog(CapturedImage image, int releaseX, int releaseY)
    {
        var dialog = new CaptureResultWindow(image, CurrentDestination());
        dialog.ActionRequested += OnResultAction;
        dialog.WorkFolderChangeRequested += ChangeWorkFolder;
        WindowPlacement.PlaceNear(dialog, dialog.PrimaryButton, releaseX, releaseY);
        dialog.Show();
    }

    private void OnResultAction(CaptureResultWindow dialog, CaptureAction action)
    {
        var image = dialog.Image;
        switch (action)
        {
            case CaptureAction.Copy:
                if (_output.CopyToClipboard(image.Source, dialog)) dialog.CloseAfterAction();
                break;
            case CaptureAction.Save:
                if (SaveToDestination(_ => image.Source, dialog)) dialog.CloseAfterAction();
                break;
            case CaptureAction.SaveAs:
                if (SaveAs(image.Source, dialog)) dialog.CloseAfterAction();
                break;
            case CaptureAction.Edit:
                dialog.CloseAfterAction();
                OpenEditor(image);
                break;
            case CaptureAction.Retry:
                dialog.CloseAfterAction();
                StartCapture(MenuCloseDelayMs);
                break;
            case CaptureAction.Close:
                break;
        }
        if (action != CaptureAction.Edit && !dialog.IsVisible) ReleaseMemorySoon();
    }

    // ---- エディタ ----

    private void OpenEditor(CapturedImage image)
    {
        var (cx, cy) = image.ScreenCenter;
        var monitor = DisplayInfo.MonitorAt(cx, cy);
        var editor = new EditorWindow(image, _settings.Editor, CurrentDestination());
        editor.SizeToImage(monitor.DpiScale);
        WindowPlacement.CenterOnMonitor(editor, cx, cy);
        editor.OutputRequested += OnEditorOutput;
        editor.WorkFolderChangeRequested += ChangeWorkFolder;
        editor.Closed += (_, _) =>
        {
            _settings.Editor = editor.Defaults;
            SaveSettingsQuietly();
            ReleaseMemorySoon();
        };
        editor.Show();
        editor.Activate();
    }

    private void OnEditorOutput(EditorWindow editor, CaptureAction action)
    {
        // 文字の入力とドラッグを確定してから出力する。保存ダイアログ・説明の入力画面を開く前にも確定しておく。
        try { editor.PrepareOutput(); }
        catch (Exception ex) when (IsCompositionError(ex))
        {
            ReportCompositionError(editor, ex);
            return;
        }

        bool ok;
        switch (action)
        {
            case CaptureAction.Copy:
                ok = ComposeOrReport(editor, editor) is { } copied && _output.CopyToClipboard(copied, editor);
                break;
            case CaptureAction.Save:
                // 連番＋説明では、説明を確定してから合成する。
                ok = SaveToDestination(owner => ComposeOrReport(editor, owner), editor);
                break;
            case CaptureAction.SaveAs:
                var target = _output.ChooseSaveAs(editor);
                if (target is null) return;
                var path = ComposeOrReport(editor, editor) is { } composed ? _output.SaveAsToTarget(composed, target, editor) : null;
                if (path is not null) RememberWorkFolder(path, editor);
                ok = path is not null;
                break;
            default:
                ok = false;
                break;
        }
        if (ok) editor.Close();
    }

    /// <summary>確定済みの編集内容を合成する。失敗したらメッセージを出して null。</summary>
    private static BitmapSource? ComposeOrReport(EditorWindow editor, Window? owner)
    {
        try
        {
            return editor.ComposePrepared();
        }
        catch (Exception ex) when (IsCompositionError(ex))
        {
            ReportCompositionError(owner ?? editor, ex);
            return null;
        }
    }

    private static bool IsCompositionError(Exception ex) =>
        ex is InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.COMException;

    private static void ReportCompositionError(Window owner, Exception ex)
    {
        ErrorLog.Write("Image composition failed", ex);
        MessageBox.Show(owner, Strings.ComposeFailed, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---- 保存先に保存・名前を付けて保存・作業用フォルダ（V1.3.0） ----

    /// <summary>
    /// 「保存先に保存」（仕様書 §12.6）。保存対象は押した時点の設定で決める。連番＋説明では説明の入力画面を出す。
    /// <paramref name="image"/> は保存の直前に呼ぶ（引数はエラー表示の親）。null を返したら中止（エラーは表示済み）。
    /// 保存できたら true。失敗・キャンセルでは false（呼び出し側は画像を残す）。
    /// </summary>
    private bool SaveToDestination(Func<Window?, BitmapSource?> image, Window? owner)
    {
        var plan = _output.PlanSave();
        if (plan.Mode != FileNamingMode.SequentialWithDescription)
            return image(owner) is { } source && _output.Save(source, plan, null, owner) is not null;

        BitmapSource? composed = null;
        var dialog = new DescriptionDialog(plan, owner, (description, dialogWindow) =>
        {
            composed ??= image(dialogWindow);
            return composed is not null && _output.Save(composed, plan, description, dialogWindow) is not null;
        });
        return dialog.ShowDialog() == true;
    }

    /// <summary>「名前を付けて保存」。保存できたら、保存したフォルダを作業用フォルダにする。</summary>
    private bool SaveAs(BitmapSource image, Window? owner)
    {
        var path = _output.SaveAs(image, owner);
        if (path is null) return false;
        RememberWorkFolder(path, owner);
        return true;
    }

    private void RememberWorkFolder(string savedPath, Window? owner)
    {
        var folder = Path.GetDirectoryName(savedPath);
        if (!string.IsNullOrEmpty(folder)) SetWorkFolder(folder, owner, afterImageSaved: true);
    }

    /// <summary>作業用フォルダ（連番の保存先）を選ぶ（キャプチャー後ダイアログ・エディタの［フォルダ変更］）。キャンセルでは変えない。</summary>
    public void ChangeWorkFolder(Window? owner)
    {
        if (_choosingWorkFolder) return;
        _choosingWorkFolder = true;
        try
        {
            var dialog = new OpenFolderDialog { Title = Strings.WorkFolderSelectTitle };
            var current = _settings.ResolveWorkFolder();
            if (Directory.Exists(current)) dialog.InitialDirectory = current;
            var chosen = owner is not null && owner.IsVisible ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (chosen == true) SetWorkFolder(dialog.FolderName, owner, afterImageSaved: false);
        }
        finally
        {
            _choosingWorkFolder = false;
        }
    }

    /// <summary>
    /// 作業用フォルダを変えて保存し、開いている画面へ反映する（作業用フォルダはここでだけ変える）。設定の保存フォルダは変えない。
    /// 設定ファイルに書けなくても、この起動中は変更後のフォルダを使う。
    /// </summary>
    private void SetWorkFolder(string folder, Window? owner, bool afterImageSaved)
    {
        if (SamePath(folder, _settings.ResolveWorkFolder())) return; // 変わらない（未指定のまま保存フォルダを使う場合も含む）
        _settings.WorkFolder = folder;
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Failed to save the working folder", ex);
            var message = string.Format(afterImageSaved ? Strings.WorkFolderSaveFailedAfterImageFormat : Strings.WorkFolderSaveFailedFormat, ex.Message);
            if (owner is not null && owner.IsVisible)
                MessageBox.Show(owner, message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(message, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshDestinations();
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>開いているキャプチャー後ダイアログ・エディタ・設定画面の保存先の表示を更新する。</summary>
    private void RefreshDestinations()
    {
        var destination = CurrentDestination();
        foreach (var window in _app.Windows.Cast<Window>())
        {
            if (window is CaptureResultWindow result) result.ShowDestination(destination);
            else if (window is EditorWindow editor) editor.ShowDestination(destination);
        }
        WorkFolderChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- 設定 ----

    public void ShowSettings()
    {
        if (!App.EnsureExecutablePresent()) return;
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void SuspendHotkey() => _hotkey?.Suspend();

    public void ResumeHotkey() => _hotkey?.Resume();

    public IReadOnlyList<string> ApplySettings(AppSettings settings, bool autoStart, string? workFolder)
    {
        var failures = new List<string>();
        settings.Editor = _settings.Editor.Clone(); // エディタの既定値は設定画面では変えない
        // 連番の保存先は、設定画面で入力した場合だけ変える。入力していなければ、開いている間に別の画面で変わった値を古い値で戻さない。
        settings.WorkFolder = workFolder ?? _settings.WorkFolder;
        SettingsNormalizer.Normalize(settings);
        _settings = settings;

        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Failed to save the settings", ex);
            failures.Add(string.Format(Strings.SettingsSaveFailedFormat, ex.Message));
        }

        // 名前の付け方・保存先の変更を、開いている画面へ反映する（再起動は不要）。
        RefreshDestinations();

        var registration = RegisterHotkeyFromSettings();
        if (registration.Status == HotkeyStatus.Failed)
            failures.Add(string.Format(Strings.HotkeyRegisterFailedFormat, registration.Describe()));

        if (autoStart != AutoStartService.IsEnabled() && AutoStartService.SetEnabled(autoStart) is { } autoStartError)
            failures.Add(string.Format(Strings.AutoStartFailedFormat, autoStartError));

        return failures;
    }

    public bool OfferRestartIfLanguageChanged(Window owner)
    {
        var wanted = UiLanguage.Resolve(_settings.Language, _windowsUiCulture);
        if (wanted.Name == _uiCulture.Name) return false;

        // 選んだ言語で尋ねる（今の言語が読めない利用者でも分かるように）。
        var message = UiLanguage.Get(nameof(Strings.RestartPrompt), wanted);
        if (_app.Windows.OfType<Window>().Any(w => w is EditorWindow or CaptureResultWindow))
            message += "\n\n" + UiLanguage.Get(nameof(Strings.RestartPromptOpenWindows), wanted);
        var answer = MessageBox.Show(owner, message, "AreaShot", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer == MessageBoxResult.Yes && Restart();
    }

    /// <summary>
    /// 新しいプロセスを --restart 付きで起動してから終了する（仕様書 §14.4）。
    /// 新しいプロセスは、このプロセスが多重起動防止を解放するまで待ってから起動する。
    /// </summary>
    private bool Restart()
    {
        if (!App.EnsureExecutablePresent()) return true;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) throw new InvalidOperationException("The executable path is not available.");
            Process.Start(new ProcessStartInfo(exe, App.RestartArgument) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            ErrorLog.Write("Restart failed", ex);
            MessageBox.Show(Strings.RestartFailed, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        ErrorLog.Write($"Restarting to change the display language: {_uiCulture.Name} → {UiLanguage.Resolve(_settings.Language, _windowsUiCulture).Name}");
        Exit();
        return true;
    }

    private void SaveSettingsQuietly()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Failed to save the settings", ex);
        }
    }

    // ---- その他 ----

    private void NotifySaved(string path)
    {
        if (_settings.ShowSaveNotification) ToastWindow.ShowSaved(path);
    }

    public void NotifyAlreadyRunning() =>
        _tray?.ShowBalloon("AreaShot", Strings.AlreadyRunning, warning: false);

    /// <summary>想定外の例外の後始末。範囲選択の全画面ウィンドウが残って操作できなくなるのを防ぐ。</summary>
    public void RecoverFromError()
    {
        try { _overlay?.Cancel(); }
        catch (Exception ex) { ErrorLog.Write("Cleanup of the capture overlay failed", ex); }
        _overlay = null;
    }

    /// <summary>
    /// 仮想画面の選択完了後や画像画面を閉じた後に、重複する要求をまとめて GC を促す。
    /// </summary>
    private void ReleaseMemorySoon()
    {
        if (_memoryCollectionQueued) return;
        _memoryCollectionQueued = true;
        _app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _memoryCollectionQueued = false;
            GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        });
    }

    private void Exit()
    {
        if (!App.EnsureExecutablePresent(notify: false)) return;
        foreach (Window w in _app.Windows.Cast<Window>().ToList())
        {
            try { w.Close(); }
            catch (InvalidOperationException) { }
        }
        _app.Shutdown(0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _delayedCapture?.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
    }
}
