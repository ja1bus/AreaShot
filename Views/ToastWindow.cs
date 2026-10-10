using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static AreaShot.Interop.NativeMethods;
using AreaShot.Core.Localization;

namespace AreaShot.Views;

/// <summary>
/// 保存完了・コピー完了の軽量な通知（仕様書 §12.4）。画面右下に数秒だけ表示し、フォーカスを奪わない。
/// 保存の通知をクリックすると、保存したファイルをエクスプローラーで選択した状態で開く。
/// OS のトースト通知は AppUserModelID の登録などが必要になるため使わない。
/// </summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer;
    private readonly string? _path;

    /// <param name="path">保存したファイル。null はコピーの通知（クリックで閉じるだけ）。</param>
    private ToastWindow(string title, string detail, string? path, TimeSpan duration)
    {
        _path = path;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        SizeToContent = SizeToContent.Height;
        Width = 380;
        Cursor = path is null ? Cursors.Arrow : Cursors.Hand;
        UseLayoutRounding = true;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
        });
        if (path is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Strings.ToastOpenFolder,
                Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10, 14, 10),
            Child = panel,
        };
        FontFamily = UiFont.Family;
        FontSize = 12.5;

        _timer = new DispatcherTimer { Interval = duration };
        _timer.Tick += (_, _) => FadeOut();
        MouseLeftButtonUp += (_, _) => OpenFolder();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) => _timer.Start();

        // 主モニターの作業領域の右下（DIU）。高さは内容で決まるため、大きさが決まるたびに下端を合わせる。
        var work = SystemParameters.WorkArea;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = work.Right - Width - 16;
        Top = work.Bottom - 120;
        SizeChanged += (_, _) => Top = work.Bottom - ActualHeight - 16;
    }

    public static void ShowSaved(string path) =>
        Present(new ToastWindow(Strings.ToastSavedTitle, string.Format(Strings.ToastSavedFormat, path), path, TimeSpan.FromSeconds(4)));

    /// <summary>キーを押しながら離してコピーしたときの通知（ダイアログが出ないため、完了が分かるようにする）。</summary>
    public static void ShowCopied() =>
        Present(new ToastWindow(Strings.ToastCopiedTitle, Strings.ToastCopiedDetail, null, TimeSpan.FromSeconds(2)));

    private static void Present(ToastWindow toast)
    {
        _current?.Close();
        _current = toast;
        toast.Closed += (_, _) => { if (_current == toast) _current = null; };
        toast.Show();
    }

    public static void CloseCurrent() => _current?.Close();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    private void FadeOut()
    {
        _timer.Stop();
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    private void OpenFolder()
    {
        if (_path is null)
        {
            Close();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Services.ErrorLog.Write("Failed to start Explorer", ex);
        }
        Close();
    }
}
