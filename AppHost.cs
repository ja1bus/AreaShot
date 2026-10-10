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

    private string SaveTooltip =>
        string.Format(Strings.SaveTooltipFormat, Path.Combine(_settings.ResolveSaveFolder(),
            FileNameTemplate.Format(_settings.FileNameTemplate, DateTime.Now) + AppSettings.ExtensionOf(_settings.ImageFormat)));

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
            QuickAction.Save => _output.SaveToDefault(image.Source, null) is not null,
            QuickAction.SaveAs => _output.SaveAs(image.Source, null) is not null,
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
        var dialog = new CaptureResultWindow(image, SaveTooltip);
        dialog.ActionRequested += OnResultAction;
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
                if (_output.SaveToDefault(image.Source, dialog) is not null) dialog.CloseAfterAction();
                break;
            case CaptureAction.SaveAs:
                if (_output.SaveAs(image.Source, dialog) is not null) dialog.CloseAfterAction();
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
        var editor = new EditorWindow(image, _settings.Editor, SaveTooltip);
        editor.SizeToImage(monitor.DpiScale);
        WindowPlacement.CenterOnMonitor(editor, cx, cy);
        editor.OutputRequested += OnEditorOutput;
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
        SaveAsTarget? target = null;
        if (action == CaptureAction.SaveAs)
        {
            try { editor.PrepareOutput(); }
            catch (Exception ex) when (IsCompositionError(ex))
            {
                ReportCompositionError(editor, ex);
                return;
            }
            target = _output.ChooseSaveAs(editor);
            if (target is null) return;
        }

        BitmapSource composed;
        try
        {
            composed = target is null ? editor.Compose() : editor.ComposePrepared();
        }
        catch (Exception ex) when (IsCompositionError(ex))
        {
            ReportCompositionError(editor, ex);
            return;
        }
        var ok = action switch
        {
            CaptureAction.Copy => _output.CopyToClipboard(composed, editor),
            CaptureAction.Save => _output.SaveToDefault(composed, editor) is not null,
            CaptureAction.SaveAs => _output.SaveAsToTarget(composed, target!, editor) is not null,
            _ => false,
        };
        if (ok) editor.Close();
    }

    private static bool IsCompositionError(Exception ex) =>
        ex is InvalidOperationException or OutOfMemoryException or System.Runtime.InteropServices.COMException;

    private static void ReportCompositionError(Window editor, Exception ex)
    {
        ErrorLog.Write("Image composition failed", ex);
        MessageBox.Show(editor, Strings.ComposeFailed, "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    public IReadOnlyList<string> ApplySettings(AppSettings settings, bool autoStart)
    {
        var failures = new List<string>();
        settings.Editor = _settings.Editor.Clone(); // エディタの既定値は設定画面では変えない
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
