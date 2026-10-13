using System.Windows;
using System.Windows.Input;
using AreaShot.Capture;

namespace AreaShot.Views;

/// <summary>キャプチャー直後の操作（仕様書 §6）。</summary>
public enum CaptureAction
{
    Copy,
    Save,
    SaveAs,
    Edit,
    Retry,
    Close,
}

/// <summary>
/// キャプチャー直後のダイアログ（仕様書 §6）。操作の実行は <see cref="ActionRequested"/> で AppHost に任せる。
/// 閉じる（×・Esc）はキャプチャー画像の破棄だけで、アプリは終了しない。
/// </summary>
public partial class CaptureResultWindow : Window
{
    /// <summary>表示直後のクリックを無視する時間。ダイアログはマウスの下に出るため、範囲選択の続きの連打で押さないようにする。</summary>
    private const int ClickGuardMs = 300;

    private bool _actionHandled;
    private long _shownAt = long.MaxValue;

    public CaptureResultWindow(CapturedImage image, SaveDestination destination)
    {
        InitializeComponent();
        Image = image;
        Preview.Source = image.Source;
        SizeText.Text = $"{image.Width} × {image.Height} px";
        ShowDestination(destination);
        Destination.ChangeRequested += (_, _) => WorkFolderChangeRequested?.Invoke(this);
    }

    public CapturedImage Image { get; }

    /// <summary>［変更...］が押された。作業用フォルダの選択と、開いている画面への反映は AppHost が行う（V1.3.0）。</summary>
    public event Action<Window>? WorkFolderChangeRequested;

    /// <summary>保存先の表示と「保存先に保存」のツールチップを更新する（作業用フォルダ・設定の変更時）。</summary>
    public void ShowDestination(SaveDestination destination)
    {
        Destination.Show(destination);
        SaveButton.ToolTip = destination.SaveTooltip;
    }

    /// <summary>最下段の左のコピーボタン。マウスを離した点に合わせてダイアログを置くために使う。</summary>
    public FrameworkElement PrimaryButton => CopyButton;

    /// <summary>操作が選ばれた。処理が成功したら AppHost が <see cref="CloseAfterAction"/> を呼ぶ。</summary>
    public event Action<CaptureResultWindow, CaptureAction>? ActionRequested;

    /// <summary>操作の完了後に閉じる（閉じたことを Close 操作として通知しない）。</summary>
    public void CloseAfterAction()
    {
        _actionHandled = true;
        Close();
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (Environment.TickCount64 - _shownAt < ClickGuardMs) e.Handled = true;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _shownAt = Environment.TickCount64; // 画面に出た時点から数える（それまでのクリックも無視する）
        Activate();
        CopyButton.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_actionHandled) ActionRequested?.Invoke(this, CaptureAction.Close);
        Preview.Source = null;
    }

    private void OnCopy(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, CaptureAction.Copy);
    private void OnSave(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, CaptureAction.Save);
    private void OnSaveAs(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, CaptureAction.SaveAs);
    private void OnEdit(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, CaptureAction.Edit);
    private void OnRetry(object sender, RoutedEventArgs e) => ActionRequested?.Invoke(this, CaptureAction.Retry);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
