using System.Windows.Forms;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

/// <summary>
/// タスクトレイのアイコンとメニュー（仕様書 §3）。WinForms の NotifyIcon を使う（WindowsDesktop 共有フレームワーク内）。
/// メニュー: キャプチャー開始 / 前回と同じ範囲でキャプチャー（V1.4.0）/ 設定 / 終了。アイコンのダブルクリックでもキャプチャーを開始する。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private readonly ToolStripMenuItem _captureItem;
    private readonly ToolStripMenuItem _repeatItem;
    private readonly System.Drawing.Font _boldFont;

    public TrayService()
    {
        _ownedIcon = LoadAppIcon();
        var menu = new ContextMenuStrip();
        _boldFont = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold);
        _captureItem = new ToolStripMenuItem(Strings.TrayCapture, null, (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = _boldFont,
        };
        menu.Items.Add(_captureItem);
        // 前回と同じ範囲（V1.4.0）。記憶が無い間は無効。
        _repeatItem = new ToolStripMenuItem(Strings.TrayRepeatCapture, null, (_, _) => RepeatCaptureRequested?.Invoke(this, EventArgs.Empty))
        {
            Enabled = false,
        };
        menu.Items.Add(_repeatItem);
        menu.Items.Add(new ToolStripMenuItem(Strings.TraySettings, null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.TrayExit, null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new NotifyIcon
        {
            Icon = _ownedIcon ?? System.Drawing.SystemIcons.Application,
            Text = "AreaShot",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CaptureRequested;
    public event EventHandler? RepeatCaptureRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    /// <summary>「前回と同じ範囲でキャプチャー」の有効・無効（範囲を記憶している間だけ有効。V1.4.0）。</summary>
    public void SetRepeatAvailable(bool available) => _repeatItem.Enabled = available;

    /// <summary>ショートカットの表示を更新する（メニューとツールチップ）。</summary>
    public void UpdateHotkeys(HotkeyRegistration capture, HotkeyRegistration repeat)
    {
        _captureItem.ShortcutKeyDisplayString = MenuText(capture);
        _repeatItem.ShortcutKeyDisplayString = MenuText(repeat);
        _icon.Text = TooltipText(capture, repeat);
    }

    /// <summary>トレイのツールチップ。登録できたショートカットだけを行ごとに出す（127 文字まで）。</summary>
    public static string TooltipText(HotkeyRegistration capture, HotkeyRegistration repeat)
    {
        var lines = new List<string> { "AreaShot" };
        if (capture.Status == HotkeyStatus.Registered) lines.Add(string.Format(Strings.TrayTipCaptureFormat, capture.Shortcut));
        if (repeat.Status == HotkeyStatus.Registered) lines.Add(string.Format(Strings.TrayTipRepeatFormat, repeat.Shortcut));
        var tip = string.Join("\n", lines);
        return tip.Length > 127 ? tip[..127] : tip; // NotifyIcon.Text は 127 文字まで
    }

    private static string MenuText(HotkeyRegistration registration) => registration.Status switch
    {
        HotkeyStatus.Registered => registration.Shortcut.ToString(),
        HotkeyStatus.Failed => string.Format(Strings.TrayHotkeyFailedFormat, registration.Shortcut),
        _ => "",
    };

    public void ShowBalloon(string title, string text, bool warning)
    {
        _icon.ShowBalloonTip(5000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
        _boldFont.Dispose();
    }

    /// <summary>exe に埋め込まれたアイコン（csproj の ApplicationIcon）を通知領域の大きさで取り出す。</summary>
    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return null;
            return System.Drawing.Icon.ExtractIcon(path, 0, SystemInformation.SmallIconSize.Width);
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
            return null;
        }
    }
}
