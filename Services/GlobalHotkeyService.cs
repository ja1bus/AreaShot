using System.Runtime.InteropServices;
using System.Windows.Interop;
using AreaShot.Core.Input;
using static AreaShot.Interop.NativeMethods;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

public enum HotkeyStatus
{
    Unassigned,
    Registered,
    /// <summary>他のアプリまたは Windows が使用中などで登録できなかった。</summary>
    Failed,
    /// <summary>設定画面での入力中のため一時的に解除している。</summary>
    Suspended,
}

/// <summary>グローバルショートカットの種類（V1.4.0）。値は RegisterHotKey の id に使う。</summary>
public enum HotkeyKind
{
    /// <summary>キャプチャー開始。</summary>
    Capture = 1,
    /// <summary>前回と同じ範囲でキャプチャー。</summary>
    RepeatCapture = 2,
}

public sealed record HotkeyRegistration(Shortcut Shortcut, HotkeyStatus Status, int ErrorCode = 0)
{
    public static readonly HotkeyRegistration Unassigned = new(Shortcut.None, HotkeyStatus.Unassigned);

    public string Describe() => Status switch
    {
        HotkeyStatus.Unassigned => Strings.HotkeyUnassigned,
        HotkeyStatus.Registered => string.Format(Strings.HotkeyRegisteredFormat, Shortcut),
        HotkeyStatus.Suspended => Strings.HotkeySuspended,
        _ when ErrorCode == ERROR_HOTKEY_ALREADY_REGISTERED => string.Format(Strings.HotkeyInUseFormat, Shortcut),
        _ => string.Format(Strings.HotkeyErrorFormat, Shortcut, ErrorCode),
    };
}

/// <summary>
/// Win32 RegisterHotKey によるグローバルショートカット（仕様書 §4）。キャプチャー開始と前回と同じ範囲（V1.4.0）の 2 本を Id ごとに扱う。
/// メッセージ専用ウィンドウで WM_HOTKEY を受ける（ポーリングしない）。
/// 登録し直すときは「全件を解除 → 全件を登録」の順にする（2 本を入れ替えても、まだ相手が使っている組み合わせで失敗しないように）。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    public static readonly IReadOnlyList<HotkeyKind> Kinds = [HotkeyKind.Capture, HotkeyKind.RepeatCapture];

    private readonly HwndSource _source;
    private readonly Dictionary<HotkeyKind, Shortcut> _shortcuts = [];
    private readonly HashSet<HotkeyKind> _registered = [];
    private readonly Dictionary<HotkeyKind, HotkeyRegistration> _current = [];
    private int _suspendCount;

    public GlobalHotkeyService()
    {
        var p = new HwndSourceParameters("AreaShot.Hotkey") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>ショートカットが押された（UI スレッド）。</summary>
    public event Action<HotkeyKind>? Pressed;

    /// <summary>登録状態が変わった。</summary>
    public event EventHandler? StatusChanged;

    /// <summary>メッセージを受けるウィンドウ（試験用）。</summary>
    public IntPtr Handle => _source.Handle;

    public HotkeyRegistration Get(HotkeyKind kind) =>
        _current.TryGetValue(kind, out var r) ? r : HotkeyRegistration.Unassigned;

    /// <summary>2 本のショートカットを差し替えて登録し直す（全件を解除してから全件を登録する）。</summary>
    public void ApplyAll(Shortcut capture, Shortcut repeat)
    {
        _shortcuts[HotkeyKind.Capture] = capture;
        _shortcuts[HotkeyKind.RepeatCapture] = repeat;
        if (_suspendCount > 0)
        {
            foreach (var kind in Kinds) SetSuspendedState(kind);
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        RegisterAll();
    }

    /// <summary>一時的に解除する（設定画面のショートカット入力中）。<see cref="Resume"/> と対で呼ぶ。</summary>
    public void Suspend()
    {
        if (_suspendCount++ > 0) return;
        UnregisterAll();
        foreach (var kind in Kinds) SetSuspendedState(kind);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        if (_suspendCount == 0 || --_suspendCount > 0) return;
        RegisterAll();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private Shortcut ShortcutOf(HotkeyKind kind) => _shortcuts.TryGetValue(kind, out var s) ? s : Shortcut.None;

    private void SetSuspendedState(HotkeyKind kind)
    {
        var sc = ShortcutOf(kind);
        _current[kind] = sc.IsEmpty ? HotkeyRegistration.Unassigned : new HotkeyRegistration(sc, HotkeyStatus.Suspended);
    }

    private void RegisterAll()
    {
        UnregisterAll();
        foreach (var kind in Kinds)
        {
            var sc = ShortcutOf(kind);
            if (sc.IsEmpty)
            {
                _current[kind] = HotkeyRegistration.Unassigned;
                continue;
            }
            // MOD_NOREPEAT: 押しっぱなしの自動リピートでキャプチャーが連続しないようにする。
            if (RegisterHotKey(_source.Handle, (int)kind, (int)sc.Modifiers | MOD_NOREPEAT, sc.Key))
            {
                _registered.Add(kind);
                _current[kind] = new HotkeyRegistration(sc, HotkeyStatus.Registered);
            }
            else
            {
                _current[kind] = new HotkeyRegistration(sc, HotkeyStatus.Failed, Marshal.GetLastWin32Error());
            }
        }
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UnregisterAll()
    {
        foreach (var kind in _registered) UnregisterHotKey(_source.Handle, (int)kind);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt64();
            foreach (var kind in Kinds)
            {
                if (id != (long)kind) continue;
                handled = true;
                Pressed?.Invoke(kind);
                break;
            }
        }
        return IntPtr.Zero;
    }
}
