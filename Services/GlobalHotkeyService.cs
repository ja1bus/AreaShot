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
/// Win32 RegisterHotKey によるキャプチャー開始のグローバルショートカット（仕様書 §4）。
/// メッセージ専用ウィンドウで WM_HOTKEY を受ける（ポーリングしない）。
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 1;

    private readonly HwndSource _source;
    private Shortcut _shortcut;
    private bool _registered;
    private int _suspendCount;

    public GlobalHotkeyService()
    {
        var p = new HwndSourceParameters("AreaShot.Hotkey") { ParentWindow = HWND_MESSAGE, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>ショートカットが押された（UI スレッド）。</summary>
    public event EventHandler? Pressed;

    /// <summary>登録状態が変わった。</summary>
    public event EventHandler? StatusChanged;

    public HotkeyRegistration Current { get; private set; } = HotkeyRegistration.Unassigned;

    /// <summary>ショートカットを差し替えて登録し直す。</summary>
    public HotkeyRegistration Apply(Shortcut shortcut)
    {
        _shortcut = shortcut;
        if (_suspendCount > 0)
        {
            SetCurrent(shortcut.IsEmpty ? HotkeyRegistration.Unassigned : new HotkeyRegistration(shortcut, HotkeyStatus.Suspended));
            return Current;
        }
        Register();
        return Current;
    }

    /// <summary>一時的に解除する（設定画面のショートカット入力中）。<see cref="Resume"/> と対で呼ぶ。</summary>
    public void Suspend()
    {
        if (_suspendCount++ > 0) return;
        Unregister();
        if (!_shortcut.IsEmpty) SetCurrent(new HotkeyRegistration(_shortcut, HotkeyStatus.Suspended));
    }

    public void Resume()
    {
        if (_suspendCount == 0 || --_suspendCount > 0) return;
        Register();
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void Register()
    {
        Unregister();
        if (_shortcut.IsEmpty)
        {
            SetCurrent(HotkeyRegistration.Unassigned);
            return;
        }
        // MOD_NOREPEAT: 押しっぱなしの自動リピートでキャプチャーが連続しないようにする。
        _registered = RegisterHotKey(_source.Handle, HotkeyId, (int)_shortcut.Modifiers | MOD_NOREPEAT, _shortcut.Key);
        SetCurrent(_registered
            ? new HotkeyRegistration(_shortcut, HotkeyStatus.Registered)
            : new HotkeyRegistration(_shortcut, HotkeyStatus.Failed, Marshal.GetLastWin32Error()));
    }

    private void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private void SetCurrent(HotkeyRegistration registration)
    {
        Current = registration;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
