using System.Globalization;
using System.IO;
using System.Windows;
using AreaShot.Interop;
using System.Windows.Threading;
using AreaShot.Services;
using AreaShot.Core.Localization;

namespace AreaShot;

/// <summary>
/// 起動・多重起動防止・未処理例外の扱い（仕様書 §3・§17）。
/// 通常のウィンドウは出さず、タスクトレイに常駐する（ShutdownMode = OnExplicitShutdown。トレイの「終了」でだけ終わる）。
/// </summary>
public partial class App : Application
{
    /// <summary>表示言語の変更で再起動したときの引数。前のプロセスが終わるまで多重起動防止の解放を待つ。</summary>
    public const string RestartArgument = "--restart";

    /// <summary>再起動のとき、前のプロセスの終了を待つ時間（編集中の画面を閉じる・設定の保存を含む）。</summary>
    private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(15);

    private SingleInstance? _instance;
    private AppHost? _host;
    private bool _started;
    private bool _sessionEnding;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        // Windows のサインアウト・再起動中はエラー画面を出さない（出すと再起動を止めてしまう）。
        SessionEnding += (_, _) => _sessionEnding = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("Unhandled exception (terminating)", args.ExceptionObject as Exception);

        // 表示言語を決める前の Windows の表示言語（AppHost が設定の言語を適用すると変わるため、先に取っておく）。
        var windowsUiCulture = CultureInfo.CurrentUICulture;
        var restarting = e.Args.Contains(RestartArgument, StringComparer.OrdinalIgnoreCase);
        _instance = SingleInstance.TryAcquire(restarting ? RestartWait : TimeSpan.Zero);
        if (_instance is null)
        {
            // 既に起動済み: 既存側がトレイの通知で知らせる。こちらは終了する。
            Shutdown(0);
            return;
        }

        _host = new AppHost(this, windowsUiCulture);
        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(() => _host?.NotifyAlreadyRunning()));
        _host.Start();
        _started = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_exiting) return; // 終了処理中に続けて起きた例外（同じ原因）は記録もしない
        ErrorLog.Write("Unhandled exception", e.Exception);
        if (IsExecutableMissing())
        {
            // 操作の開始前の確認（EnsureExecutablePresent）をすり抜けた場合。画面の処理の途中で壊れているため、
            // 案内の画面も正しく出せない（出そうとすると異常終了した）。記録だけして終了する。
            ExitImmediately(1);
            return;
        }
        if (_sessionEnding)
        {
            ExitImmediately(0);
            return;
        }
        try
        {
            // 常駐アプリなので、画面操作中の想定外の例外ではプロセスを終了しない（その操作だけ失敗させる）。
            _host?.RecoverFromError();
            MessageBox.Show(
                string.Format(Strings.AppUnexpectedErrorFormat, e.Exception.GetType().Name, e.Exception.Message, ErrorLog.FilePath) +
                (_started ? "" : "\n\n" + Strings.AppWillExit),
                "AreaShot", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Failed to show the error message", ex);
        }
        if (!_started)
        {
            // 起動途中の失敗ではプロセスを残さない（local/C# の既知の落とし穴）。
            _host?.Dispose();
            Shutdown(1);
        }
    }

    /// <summary>
    /// 実行中に exe が移動・削除されたか。
    /// 単一exe（非圧縮）は、未読み込みの DLL を起動時のパスの exe から後で読み込む。そのパスに exe が無いと
    /// メッセージが空の FileNotFoundException になる（2026-09-23、別PCで exe を移動した後に発生・再現確認）。
    /// TypeInitializationException などに包まれて届くこともあるため、例外の種類ではなく exe の有無で判定する。
    /// </summary>
    private static bool IsExecutableMissing()
    {
        var path = Environment.ProcessPath;
        return !string.IsNullOrEmpty(path) && !File.Exists(path);
    }

    /// <summary>
    /// 操作（キャプチャー・設定・終了）の開始前に呼ぶ。exe があれば true。
    /// 無ければ（<paramref name="notify"/> なら案内を出して）プロセスを終了する。
    /// 案内は WPF を使わない Win32 のメッセージボックスで出す（未読み込みの WPF の部品を読み込めないため）。
    /// </summary>
    public static bool EnsureExecutablePresent(bool notify = true)
    {
        if (!IsExecutableMissing()) return true;
        var app = (App)Current;
        app._exiting = true;
        ErrorLog.Write($"The executable was moved or deleted while running: {Environment.ProcessPath}");
        if (notify && !app._sessionEnding)
        {
            // 文字列のリソース（言語別の DLL）も読み込めない可能性があるため、ここだけはコードに直接書く。
            var message = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == UiLanguage.Japanese
                ? "AreaShot の実行ファイルが起動中に移動または削除されたため、動作を続けられません。AreaShot を終了します。\n\n" +
                  $"起動時の場所: {Environment.ProcessPath}\n\n移動した場合は、移動先の AreaShot.exe を起動し直してください。"
                : "AreaShot cannot continue because its executable was moved or deleted while it was running. AreaShot will now exit.\n\n" +
                  $"Original location: {Environment.ProcessPath}\n\nIf you moved it, start AreaShot.exe from the new location.";
            NativeMethods.MessageBox(IntPtr.Zero, message,
                "AreaShot", NativeMethods.MB_ICONWARNING | NativeMethods.MB_SETFOREGROUND | NativeMethods.MB_TOPMOST);
        }
        app.ExitImmediately(notify ? 1 : 0);
        return false;
    }

    /// <summary>
    /// 通常の Shutdown を使わずに終了する。通常の終了処理は未読み込みの DLL を読み込もうとして同じ例外になるため、
    /// トレイアイコンなどの後始末だけしてプロセスを終える。
    /// </summary>
    private void ExitImmediately(int exitCode)
    {
        _exiting = true;
        try { _host?.Dispose(); }
        catch (Exception ex) { ErrorLog.Write("Cleanup on exit failed", ex); }
        try { _instance?.Dispose(); }
        catch (Exception ex) { ErrorLog.Write("Cleanup on exit failed", ex); }
        Environment.Exit(exitCode);
    }
}
