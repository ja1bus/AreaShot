using System.Text.Json.Serialization;
using AreaShot.Core.Annotations;
using AreaShot.Core.Input;
using AreaShot.Core.Localization;
using AreaShot.Core.Naming;

namespace AreaShot.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ImageFormatKind>))]
public enum ImageFormatKind
{
    Png,
    Jpeg,
}

/// <summary>
/// ユーザー設定（仕様書 §14）。%LocalAppData%\AreaShot\settings.json に保存する。
/// 「Windows ログイン時の自動起動」はレジストリ（HKCU\...\Run）の状態そのものを正とし、ここには保存しない。
/// </summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultJpegQuality = 90;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>保存フォルダ。空なら既定（ピクチャ\AreaShot）。</summary>
    public string SaveFolder { get; set; } = "";

    public string FileNameTemplate { get; set; } = Naming.FileNameTemplate.Default;

    public ImageFormatKind ImageFormat { get; set; } = ImageFormatKind.Png;

    /// <summary>JPEG 品質（1～100）。PNG のときは使わない。</summary>
    public int JpegQuality { get; set; } = DefaultJpegQuality;

    /// <summary>キャプチャー開始のグローバルショートカット（"Ctrl+Alt+S" 形式。空は未割当）。</summary>
    public string CaptureHotkey { get; set; } = Shortcut.DefaultCapture.ToString();

    /// <summary>保存したとき・キーを押しながら離してコピーしたときの通知。</summary>
    public bool ShowSaveNotification { get; set; } = true;

    /// <summary>範囲選択でキーを押しながら離すと、ダイアログを出さずに実行する（V1.0.4）。</summary>
    public QuickActionSettings QuickActions { get; set; } = new();

    /// <summary>エディタの既定値（最後に使った値を保存する）。</summary>
    public EditorDefaults Editor { get; set; } = new();

    /// <summary>表示言語（V1.1.0）。"" は Windows の表示言語に合わせる、"en" / "ja" は固定（<see cref="UiLanguage"/>）。変更は再起動後に反映する。</summary>
    public string Language { get; set; } = UiLanguage.System;

    /// <summary>既定の保存フォルダ（ピクチャ\AreaShot）。</summary>
    public static string DefaultSaveFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AreaShot");

    /// <summary>実際に使う保存フォルダ（環境変数を展開する）。</summary>
    public string ResolveSaveFolder() =>
        string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder : Environment.ExpandEnvironmentVariables(SaveFolder.Trim());

    public static string ExtensionOf(ImageFormatKind format) => format == ImageFormatKind.Jpeg ? ".jpg" : ".png";

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Editor = Editor.Clone();
        copy.QuickActions = QuickActions.Clone();
        return copy;
    }
}

public sealed class EditorDefaults
{
    /// <summary>"#AARRGGBB"。</summary>
    public string Color { get; set; } = EditorPresets.DefaultColor.ToString();
    public double StrokeWidth { get; set; } = EditorPresets.DefaultStrokeWidth;
    public string FontFamily { get; set; } = EditorPresets.DefaultFontFamily;
    public double FontSize { get; set; } = EditorPresets.DefaultFontSize;

    public EditorDefaults Clone() => (EditorDefaults)MemberwiseClone();
}
