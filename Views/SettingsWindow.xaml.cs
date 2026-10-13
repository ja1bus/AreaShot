using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AreaShot.Core.Input;
using AreaShot.Core.Licensing;
using AreaShot.Core.Naming;
using AreaShot.Core.Settings;
using AreaShot.Services;
using Microsoft.Win32;
using AreaShot.Core.Localization;

namespace AreaShot.Views;

/// <summary>設定画面から AppHost へ依頼する操作。</summary>
public interface ISettingsHost
{
    AppSettings Settings { get; }
    HotkeyRegistration HotkeyRegistration { get; }
    event EventHandler? HotkeyRegistrationChanged;

    /// <summary>作業用フォルダ（連番の保存先）が変わった（設定画面を開いている間に別の画面で変えた場合。V1.3.0）。</summary>
    event EventHandler? WorkFolderChanged;

    void SuspendHotkey();
    void ResumeHotkey();

    /// <summary>
    /// 設定を適用・保存する。失敗した項目のメッセージを返す（成功なら空）。
    /// <paramref name="workFolder"/> は設定画面で連番の保存先を入力した場合だけ渡す（null は変えない）。
    /// </summary>
    IReadOnlyList<string> ApplySettings(AppSettings settings, bool autoStart, string? workFolder);

    /// <summary>
    /// 表示言語が今の表示と変わっていれば、再起動するか確認する（保存に成功した後に呼ぶ）。
    /// 再起動を始めた場合は true（すべての画面が閉じられる）。
    /// </summary>
    bool OfferRestartIfLanguageChanged(Window owner);
}

/// <summary>
/// 設定画面（仕様書 §14）。画面上の値は保存を押すまで適用しない。
/// ショートカットの登録に失敗した場合は、設定は保存したうえで画面を閉じずに失敗を表示する。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly AppSettings _draft;
    private bool _suspended;
    private bool _loadingWorkFolder;
    /// <summary>連番の保存先の欄を利用者が書き換えた。書き換えていなければ、別の画面での変更を表示に反映し、保存しても変えない。</summary>
    private bool _workFolderEdited;

    public SettingsWindow(ISettingsHost host)
    {
        InitializeComponent();
        _host = host;
        _draft = host.Settings.Clone();

        // 内容が作業領域より高い場合はスクロールする（［保存］［キャンセル］は下に固定）。
        MaxHeight = SystemParameters.WorkArea.Height;

        FolderBox.Text = _draft.SaveFolder;
        FolderNote.Text = FolderNoteText(AppSettings.DefaultSaveFolder);
        FolderBox.TextChanged += (_, _) => { if (!_workFolderEdited) LoadWorkFolder(); };
        LoadWorkFolder();
        NamingCombo.ItemsSource = FileNamingModes.All.Select(m => new NamingChoice(m)).ToList();
        NamingCombo.SelectedIndex = Math.Max(0, FileNamingModes.All.ToList().IndexOf(_draft.NamingMode));
        TemplateBox.Text = _draft.FileNameTemplate;
        TokenHelp.Text = string.Format(Strings.SettingsTokenHelpFormat,
            string.Join("  ", FileNameTemplate.Tokens.Select(t => $"{t.Token}={t.Description}")));
        TemplateExamples.Text = Strings.SettingsTemplateExamples + "\n" + string.Join("\n",
            TemplateExampleList.Select(t => $"  {t}  →  {FileNameTemplate.Format(t, DateTime.Now)}"));
        PngRadio.IsChecked = _draft.ImageFormat == ImageFormatKind.Png;
        JpegRadio.IsChecked = _draft.ImageFormat == ImageFormatKind.Jpeg;
        QualitySlider.Value = _draft.JpegQuality;
        Shortcut.TryParse(_draft.CaptureHotkey, out var sc);
        HotkeyBox.Shortcut = sc;
        AutoStartCheck.IsChecked = AutoStartService.IsEnabled();
        NotifyCheck.IsChecked = _draft.ShowSaveNotification;
        var quick = _draft.QuickActions;
        foreach (var action in Enum.GetValues<QuickAction>())
        {
            var combo = QuickCombo(action);
            combo.ItemsSource = ReleaseKeys.All.Select(k => new KeyChoice(k)).ToList();
            combo.SelectedIndex = Math.Max(0, ReleaseKeys.All.ToList().IndexOf(quick.KeyOf(action)));
        }
        QuickCheck.IsChecked = quick.Enabled;
        QuickHintCheck.IsChecked = quick.ShowHint;
        LanguageCombo.ItemsSource = UiLanguage.All.Select(v => new LanguageChoice(v)).ToList();
        LanguageCombo.SelectedIndex = Math.Max(0, UiLanguage.All.ToList().IndexOf(UiLanguage.Normalize(_draft.Language)));
        // csproj の <Version>（例: 1.0.2）。どの版が動いているかを利用者が確認できるようにする。
        VersionRun.Text =string.Format(Strings.SettingsVersionFormat, typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3));
        ManualLink.ToolTip = AppLinks.Manual(System.Globalization.CultureInfo.CurrentUICulture);
        SourceLink.ToolTip = AppLinks.Repository;

        HotkeyBox.CaptureStarted += (_, _) => Suspend();
        HotkeyBox.CaptureEnded += (_, _) => Resume();
        HotkeyBox.ShortcutChanged += (_, _) => ShowHotkeyStatus();
        _host.HotkeyRegistrationChanged += OnRegistrationChanged;
        _host.WorkFolderChanged += OnWorkFolderChanged;

        UpdateTemplatePreview();
        UpdateNamingPanels();
        UpdateQualityEnabled();
        ShowHotkeyStatus();
        UpdateQuickEnabled();
    }

    /// <summary>「使用できる変数・記入例」に出すテンプレートの例。</summary>
    private static readonly string[] TemplateExampleList =
        [FileNameTemplate.Default, "{yyyy}-{MM}-{dd}_{HH}{mm}{ss}", "capture_{yyyy}{MM}{dd}"];

    /// <summary>保存フォルダの下の説明（役割と、空欄の場合の保存先）。</summary>
    public static string FolderNoteText(string defaultFolder) =>
        Strings.SettingsSaveFolderRole + "\n" + string.Format(Strings.SettingsFolderNoteFormat, defaultFolder);

    /// <summary>ファイル名の付け方の選択肢（表示名は ToString）。</summary>
    private sealed record NamingChoice(FileNamingMode Mode)
    {
        public override string ToString() => FileNamingModes.Describe(Mode);
    }

    private FileNamingMode SelectedNamingMode => (NamingCombo.SelectedItem as NamingChoice)?.Mode ?? FileNamingMode.DateTemplate;

    private void OnNamingChanged(object sender, SelectionChangedEventArgs e) => UpdateNamingPanels();

    private void OnWorkFolderChanged(object? sender, EventArgs e)
    {
        if (!_workFolderEdited) LoadWorkFolder();
    }

    private void OnWorkFolderTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loadingWorkFolder) _workFolderEdited = true;
    }

    /// <summary>
    /// 連番の保存先の欄に今の値を入れる。未指定（保存フォルダを使う）の場合は、入力中の保存フォルダを見せる
    /// （書き換えずに保存すれば未指定のまま残り、保存フォルダに合わせて変わる）。
    /// </summary>
    private void LoadWorkFolder()
    {
        if (WorkFolderBox is null || FolderBox is null) return;
        var work = _host.Settings.WorkFolder;
        _loadingWorkFolder = true;
        WorkFolderBox.Text = string.IsNullOrWhiteSpace(work) ? ExpandFolder(FolderBox.Text) : Environment.ExpandEnvironmentVariables(work.Trim());
        _loadingWorkFolder = false;
    }

    private static string ExpandFolder(string text) =>
        string.IsNullOrWhiteSpace(text) ? AppSettings.DefaultSaveFolder : Environment.ExpandEnvironmentVariables(text.Trim());

    /// <summary>
    /// ファイル名テンプレートでは保存フォルダとテンプレートの欄、連番では連番の保存先と保存例を出す（V1.3.0）。
    /// 見せるフォルダは 1 つだけにする。切り替えても、どちらの入力も消さない。
    /// </summary>
    private void UpdateNamingPanels()
    {
        if (SequentialPanel is null || NamingCombo is null) return;
        var mode = SelectedNamingMode;
        var date = mode == FileNamingMode.DateTemplate;
        SaveFolderPanel.Visibility = TemplatePanel.Visibility = date ? Visibility.Visible : Visibility.Collapsed;
        WorkFolderPanel.Visibility = SequentialPanel.Visibility = date ? Visibility.Collapsed : Visibility.Visible;
        if (date) return;

        var ext = JpegRadio.IsChecked == true ? ".jpg" : ".png";
        var withDescription = mode == FileNamingMode.SequentialWithDescription;
        SequentialPreview.Text = string.Format(Strings.SettingsExampleFormat,
            SequentialFileName.BaseName(1, withDescription ? Strings.SettingsSequentialExampleDescription : null) + ext);
        SequentialNote.Text = withDescription ? Strings.SettingsSequentialNoteDescription : "";
        SequentialNote.Visibility = withDescription ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>コンボボックスの選択肢（表示名は ToString）。</summary>
    private sealed record KeyChoice(ReleaseKey Key)
    {
        public override string ToString() => ReleaseKeys.Describe(Key);
    }

    /// <summary>表示言語の選択肢（表示名は ToString）。</summary>
    private sealed record LanguageChoice(string Value)
    {
        public override string ToString() => UiLanguage.DisplayName(Value);
    }

    private ComboBox QuickCombo(QuickAction action) => action switch
    {
        QuickAction.Copy => QuickCopyCombo,
        QuickAction.Edit => QuickEditCombo,
        QuickAction.Save => QuickSaveCombo,
        _ => QuickSaveAsCombo,
    };

    private ReleaseKey SelectedKey(QuickAction action) => (QuickCombo(action).SelectedItem as KeyChoice)?.Key ?? ReleaseKey.None;

    /// <summary>画面上の割り当て（有効・案内の表示も含む）。</summary>
    private QuickActionSettings ReadQuickActions()
    {
        var q = new QuickActionSettings { Enabled = QuickCheck.IsChecked == true, ShowHint = QuickHintCheck.IsChecked == true };
        foreach (var action in Enum.GetValues<QuickAction>()) q.SetKey(action, SelectedKey(action));
        return q;
    }

    private void OnQuickEnabledChanged(object sender, RoutedEventArgs e) => UpdateQuickEnabled();

    private void OnQuickKeyChanged(object sender, SelectionChangedEventArgs e) => ShowQuickStatus();

    /// <summary>OFF でも割り当ては消さずに残し、欄を灰色にするだけにする。</summary>
    private void UpdateQuickEnabled()
    {
        if (QuickGrid is null || QuickHintCheck is null) return;
        var on = QuickCheck.IsChecked == true;
        QuickGrid.IsEnabled = on;
        QuickHintCheck.IsEnabled = on;
        ShowQuickStatus();
    }

    private void ShowQuickStatus()
    {
        if (QuickStatusText is null || QuickSaveAsCombo is null) return;
        var q = ReadQuickActions();
        var dup = q.Duplicates();
        // キーの重なりは「詳細」の外に出す（折りたたまない）。
        QuickErrorText.Text = dup.Count > 0 ? DescribeDuplicates(dup) : "";
        QuickErrorText.Visibility = dup.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QuickStatusText.Text = q.Enabled
            ? Strings.SettingsQuickStatusOn
            : Strings.SettingsQuickStatusOff;
    }

    private static string DescribeDuplicates(IEnumerable<ReleaseKey> keys) =>
        string.Format(Strings.SettingsDuplicateKeysFormat,
            string.Join(Strings.ListSeparator, keys.Select(k => string.Format(Strings.QuotedFormat, ReleaseKeys.Describe(k)))));

    protected override void OnClosed(EventArgs e)
    {
        _host.HotkeyRegistrationChanged -= OnRegistrationChanged;
        _host.WorkFolderChanged -= OnWorkFolderChanged;
        Resume();
        base.OnClosed(e);
    }

    private void Suspend()
    {
        if (_suspended) return;
        _suspended = true;
        _host.SuspendHotkey();
    }

    private void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        _host.ResumeHotkey();
    }

    private void OnRegistrationChanged(object? sender, EventArgs e) => ShowHotkeyStatus();

    /// <summary>ショートカットの状態表示。入力中の値が登録中の値と違う場合は「保存で登録」と表示する。</summary>
    private void ShowHotkeyStatus()
    {
        var input = HotkeyBox.Shortcut;
        var reg = _host.HotkeyRegistration;
        string text;
        Brush color;
        var validation = input.IsEmpty ? ShortcutValidation.Ok : input.Validate();
        if (validation != ShortcutValidation.Ok)
        {
            text = Shortcut.Describe(validation);
            color = Brushes.Firebrick;
        }
        else if (input != reg.Shortcut)
        {
            text = input.IsEmpty ? Strings.SettingsHotkeyWillUnassign : Strings.SettingsHotkeyWillRegister;
            color = Brushes.DimGray;
        }
        else
        {
            text = reg.Status == HotkeyStatus.Failed ? string.Format(Strings.SettingsHotkeyFailedFormat, reg.Describe()) : reg.Describe();
            color = reg.Status switch
            {
                HotkeyStatus.Registered => Brushes.ForestGreen,
                HotkeyStatus.Failed => Brushes.Firebrick,
                _ => Brushes.DimGray,
            };
        }
        HotkeyStatusText.Text = text;
        HotkeyStatusText.Foreground = color;
        HotkeyStatusText.FontWeight = color == Brushes.Firebrick ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.SettingsSelectFolderTitle };
        var current = string.IsNullOrWhiteSpace(FolderBox.Text) ? AppSettings.DefaultSaveFolder : Environment.ExpandEnvironmentVariables(FolderBox.Text.Trim());
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true) FolderBox.Text = dialog.FolderName;
    }

    private void OnBrowseWorkFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Strings.WorkFolderSelectTitle };
        var current = ExpandFolder(WorkFolderBox.Text);
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) == true) WorkFolderBox.Text = dialog.FolderName;
    }

    private void OnTemplateChanged(object sender, TextChangedEventArgs e) => UpdateTemplatePreview();

    private void UpdateTemplatePreview()
    {
        if (TemplatePreview is null) return;
        var error = FileNameTemplate.Validate(TemplateBox.Text);
        if (error is not null)
        {
            TemplatePreview.Text = error;
            TemplatePreview.Foreground = Brushes.Firebrick;
            return;
        }
        var ext = JpegRadio.IsChecked == true ? ".jpg" : ".png";
        TemplatePreview.Text = string.Format(Strings.SettingsExampleFormat, FileNameTemplate.Format(TemplateBox.Text, DateTime.Now) + ext);
        TemplatePreview.Foreground = Brushes.DimGray;
    }

    private void OnFormatChanged(object sender, RoutedEventArgs e)
    {
        UpdateQualityEnabled();
        UpdateTemplatePreview();
        UpdateNamingPanels();
    }

    private void UpdateQualityEnabled()
    {
        if (QualitySlider is null) return;
        var jpeg = JpegRadio.IsChecked == true;
        QualitySlider.IsEnabled = jpeg;
        QualityText.Opacity = jpeg ? 1 : 0.4;
    }

    private void OnQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (QualityText is not null) QualityText.Text = ((int)QualitySlider.Value).ToString();
    }

    private void OnResetHotkey(object sender, RoutedEventArgs e) => HotkeyBox.Shortcut = Shortcut.DefaultCapture;

    /// <summary>取扱説明書（Web）を既定のブラウザーで開く。開けなければ URL を見せる（設定画面は閉じない）。</summary>
    private void OnOpenManual(object sender, RoutedEventArgs e)
    {
        var url = AppLinks.Manual(System.Globalization.CultureInfo.CurrentUICulture);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write("Failed to open the user guide", ex);
            MessageBox.Show(this, string.Format(Strings.SettingsManualOpenFailedFormat, ex.Message, url),
                "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>同梱の LICENSE（MIT License の全文）をこの画面で表示する（V1.2.0）。読めなければフルパスを示す。</summary>
    private void OnShowLicense(object sender, RoutedEventArgs e)
    {
        var text = LicenseFiles.Read(LicenseFiles.LicenseName, out var problem);
        if (text is null)
        {
            ErrorLog.Write($"Failed to read the license file: {problem}");
            MessageBox.Show(this, string.Format(Strings.SettingsLicenseUnreadableFormat, problem),
                "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(this, text, Strings.SettingsLicenseLink, MessageBoxButton.OK, MessageBoxImage.None);
    }

    /// <summary>公開リポジトリ（ソースコード）を既定のブラウザーで開く（V1.2.0）。開けなければ URL を見せる。</summary>
    private void OnOpenSource(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLinks.Repository) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorLog.Write("Failed to open the source code page", ex);
            MessageBox.Show(this, string.Format(Strings.SettingsSourceOpenFailedFormat, ex.Message, AppLinks.Repository),
                "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 同梱の THIRD-PARTY-NOTICES.md をメモ帳で開く（V1.2.0。.md は関連付けが無いことがあるため）。
    /// 開けなければフルパスを示す（暗号化などで読めない場合に利用者が確かめられるように）。
    /// </summary>
    private void OnOpenNotices(object sender, RoutedEventArgs e)
    {
        var path = LicenseFiles.PathOf(LicenseFiles.NoticesName);
        string? problem = null;
        if (!File.Exists(path)) problem = path;
        else
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("notepad.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
                System.Diagnostics.Process.Start(psi)?.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                problem = $"{path}{Environment.NewLine}{ex.Message}";
            }
        }
        if (problem is null) return;
        ErrorLog.Write($"Failed to open the third-party notices: {problem}");
        MessageBox.Show(this, string.Format(Strings.SettingsNoticesOpenFailedFormat, problem),
            "AreaShot", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var errors = new List<string>();
        var namingMode = SelectedNamingMode;
        var templateError = FileNameTemplate.Validate(TemplateBox.Text);
        // テンプレートの欄を表示している（日時・テンプレート）ときだけ誤りにする。連番では、誤った入力は保存せず前の値を残す。
        if (templateError is not null && namingMode == FileNamingMode.DateTemplate)
            errors.Add(string.Format(Strings.SettingsErrorFileNameFormat, templateError));
        var folder = FolderBox.Text.Trim();
        if (folder.Length > 0 && folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0) errors.Add(Strings.SettingsErrorFolderInvalid);
        // 連番の保存先は書き換えた場合だけ渡す（空欄は未指定＝保存フォルダを使う）。
        var workFolder = _workFolderEdited ? WorkFolderBox.Text.Trim() : null;
        if (workFolder is { Length: > 0 } && workFolder.IndexOfAny(Path.GetInvalidPathChars()) >= 0) errors.Add(Strings.SettingsErrorWorkFolderInvalid);
        var sc = HotkeyBox.Shortcut;
        if (!sc.IsEmpty && sc.Validate() != ShortcutValidation.Ok) errors.Add(string.Format(Strings.SettingsErrorShortcutFormat, Shortcut.Describe(sc.Validate())));
        var quick = ReadQuickActions();
        if (quick.Duplicates() is { Count: > 0 } dup) errors.Add(string.Format(Strings.SettingsErrorQuickFormat, DescribeDuplicates(dup)));
        if (errors.Count > 0)
        {
            ShowErrors(errors);
            return;
        }

        _draft.SaveFolder = folder;
        _draft.NamingMode = namingMode;
        if (templateError is null) _draft.FileNameTemplate = TemplateBox.Text.Trim();
        _draft.ImageFormat = JpegRadio.IsChecked == true ? ImageFormatKind.Jpeg : ImageFormatKind.Png;
        _draft.JpegQuality = (int)QualitySlider.Value;
        _draft.CaptureHotkey = sc.ToString();
        _draft.ShowSaveNotification = NotifyCheck.IsChecked == true;
        _draft.QuickActions = quick;
        _draft.Language = (LanguageCombo.SelectedItem as LanguageChoice)?.Value ?? UiLanguage.System;

        // 入力欄にフォーカスがあると一時解除中なので、先に戻してから登録する。
        Resume();
        var failures = _host.ApplySettings(_draft.Clone(), AutoStartCheck.IsChecked == true, workFolder);
        if (failures.Count > 0)
        {
            ShowErrors(failures);
            ShowHotkeyStatus();
            return; // 閉じずに失敗を見せる（設定自体は保存済み）
        }
        if (_host.OfferRestartIfLanguageChanged(this)) return; // 再起動中（この画面も閉じられる）
        Close();
    }

    /// <summary>キャンセル: 画面上の入力（_draft も含む）は適用も保存もせずに破棄して閉じる。</summary>
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Esc でもキャンセルする。ショートカット入力欄にフォーカスがある間は
        // ShortcutBox が PreviewKeyDown で処理済みにするため、ここには届かない。
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void ShowErrors(IEnumerable<string> errors)
    {
        ErrorText.Text = string.Join("\n", errors);
        ErrorText.Visibility = Visibility.Visible;
    }
}
