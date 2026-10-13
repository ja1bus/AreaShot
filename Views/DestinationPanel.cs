using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AreaShot.Core.Localization;
using AreaShot.Core.Settings;

namespace AreaShot.Views;

/// <summary>
/// 保存先の表示に使う値（V1.3.0）。AppHost が設定から作り、連番の保存先や設定が変わるたびに開いている画面へ配る。
/// <paramref name="Folder"/> は「保存先に保存」の保存先（ファイル名テンプレートは保存フォルダ、連番は連番の保存先）。
/// </summary>
public sealed record SaveDestination(FileNamingMode Mode, string Folder, bool FolderExists, string SaveTooltip);

/// <summary>
/// キャプチャー後ダイアログとエディタの保存先の表示（V1.3.0。仕様書 §6・§9.2）。
/// 見せる保存先は 1 つだけにする。連番では横に［フォルダ変更］を出す（ファイル名テンプレートの保存先は設定画面で変える）。
/// </summary>
public sealed class DestinationPanel : Grid
{
    private readonly CompactPathText _path;
    private readonly Button _change;

    public DestinationPanel()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        foreground.Freeze();
        var label = new TextBlock { Text = Strings.DestinationFolder, Foreground = foreground, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        Children.Add(label);
        _path = new CompactPathText { Foreground = foreground, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(_path, 1);
        Children.Add(_path);
        _change = new Button
        {
            Content = Strings.DestinationChange,
            ToolTip = Strings.DestinationChangeTip,
            Padding = new Thickness(8, 1, 8, 1),
            Margin = new Thickness(6, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _change.Click += (_, _) => ChangeRequested?.Invoke(this, EventArgs.Empty);
        SetColumn(_change, 2);
        Children.Add(_change);
        MinHeight = 28; // 方式を切り替えてボタンが消えても高さを変えない
    }

    /// <summary>［フォルダ変更］が押された（フォルダの選択は AppHost が行う）。</summary>
    public event EventHandler? ChangeRequested;

    public void Show(SaveDestination destination)
    {
        var sequential = destination.Mode != FileNamingMode.DateTemplate;
        _path.Path = destination.Folder;
        // 保存フォルダは保存時に作るため、無くても知らせない。連番の保存先は利用者が選んだフォルダなので、無ければ知らせる。
        _path.Suffix = sequential && !destination.FolderExists ? Strings.DestinationMissingSuffix : "";
        _change.Visibility = sequential ? Visibility.Visible : Visibility.Collapsed;
    }
}
