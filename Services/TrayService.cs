using System.Windows.Forms;
using AreaShot.Core.Localization;

namespace AreaShot.Services;

/// <summary>
/// タスクトレイのアイコンとメニュー（仕様書 §3）。WinForms の NotifyIcon を使う（WindowsDesktop 共有フレームワーク内）。
/// メニュー: キャプチャー開始 / 設定 / 終了。アイコンのダブルクリックでもキャプチャーを開始する。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Drawing.Icon? _ownedIcon;
    private readonly ToolStripMenuItem _captureItem;

    public TrayService()
    {
        _ownedIcon = LoadAppIcon();
        var menu = new ContextMenuStrip();
        _captureItem = new ToolStripMenuItem(Strings.TrayCapture, null, (_, _) => CaptureRequested?.Invoke(this, EventArgs.Empty))
        {
            Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold),
        };
        menu.Items.Add(_captureItem);
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
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    /// <summary>ショートカットの表示を更新する（メニューとツールチップ）。</summary>
    public void UpdateHotkey(HotkeyRegistration registration)
    {
        var text = registration.Status switch
        {
            HotkeyStatus.Registered => registration.Shortcut.ToString(),
            HotkeyStatus.Failed => string.Format(Strings.TrayHotkeyFailedFormat, registration.Shortcut),
            _ => "",
        };
        _captureItem.ShortcutKeyDisplayString = text;
        var tip = text.Length == 0 ? "AreaShot" : $"AreaShot ({text})";
        _icon.Text = tip.Length > 127 ? tip[..127] : tip; // NotifyIcon.Text は 127 文字まで
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        _icon.ShowBalloonTip(5000, title, text, warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _ownedIcon?.Dispose();
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
