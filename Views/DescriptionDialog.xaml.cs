using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AreaShot.Core.Localization;
using AreaShot.Core.Naming;
using AreaShot.Services;

namespace AreaShot.Views;

/// <summary>
/// 連番＋説明で「保存先に保存」したときの説明の入力（V1.3.0。仕様書 §12.6）。
/// 保存は <c>save</c> に任せ、失敗したら画面を閉じずに説明を残す（もう一度［保存］できる）。
/// ［保存］の時点で番号が変わっていたら、新しい保存名を見せてから保存し直してもらう。
/// </summary>
public partial class DescriptionDialog : Window
{
    private readonly SavePlan _plan;
    private readonly Func<string, Window, bool> _save;
    private int _number;
    private string? _numberingError;

    /// <param name="plan">保存操作の時点で決めた保存対象（画面を開いた後に設定が変わっても変えない）。</param>
    /// <param name="save">説明と、エラー表示の親にするこの画面を受け取って保存する。保存できたら true。</param>
    public DescriptionDialog(SavePlan plan, Window? owner, Func<string, Window, bool> save)
    {
        InitializeComponent();
        _plan = plan;
        _save = save;
        if (owner is not null && owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            // キーを押しながら離して保存したときは親の画面が無い。範囲選択の後に埋もれないよう最前面に出す。
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
        }
        FolderText.Path = plan.Folder;
        Rescan();
        UpdateName();
        Loaded += (_, _) =>
        {
            Activate();
            DescriptionBox.Focus();
            Keyboard.Focus(DescriptionBox);
        };
    }

    /// <summary>今のフォルダの画像から番号を求め直す。</summary>
    private SequentialName Rescan()
    {
        var latest = ImageOutputService.PreviewSequential(_plan, DescriptionBox.Text);
        _numberingError = latest.Number == 0 ? latest.Error : null;
        if (latest.Number > 0) _number = latest.Number;
        return latest;
    }

    private void OnDescriptionChanged(object sender, TextChangedEventArgs e)
    {
        if (NameText is null) return;
        HideMessage();
        UpdateName();
    }

    /// <summary>表示中の番号と入力中の説明から保存名を作る（入力のたびにフォルダは読まない）。</summary>
    private void UpdateName()
    {
        if (_numberingError is not null)
        {
            NameText.Text = "";
            ShowMessage(_numberingError);
            return;
        }
        var fileName = SequentialFileName.BaseName(_number, DescriptionBox.Text) + _plan.Extension;
        NameText.Text = fileName;
        if (SequentialFileName.Validate(_plan.Folder, _number, fileName) is { } error) ShowMessage(error);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var shown = _number;
        var latest = Rescan();
        if (latest.Error is not null)
        {
            UpdateName();
            ShowMessage(latest.Error);
            return;
        }
        if (latest.Number != shown)
        {
            // 他の画像が保存されて番号が変わった。新しい名前を見せて、もう一度押してもらう。
            UpdateName();
            ShowMessage(Strings.DescriptionNumberChanged);
            return;
        }
        if (_save(DescriptionBox.Text, this))
        {
            DialogResult = true;
            return;
        }
        // 保存できなかった（メッセージは表示済み）。説明を残して、もう一度保存できるようにする。
        DescriptionBox.Focus();
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.Visibility = Visibility.Visible;
    }

    private void HideMessage()
    {
        MessageText.Text = "";
        MessageText.Visibility = Visibility.Collapsed;
    }
}
