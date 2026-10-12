namespace AreaShot.Core.Naming;

/// <summary>
/// 「名前を付けて保存」で入力されたパスから、実際に書くパスを決める（仕様書 §12.3）。
/// 拡張子で形式を決め、対応していない拡張子なら選んだ種類の拡張子を付け足す。
/// 付け足した場合は保存ダイアログが確認したパスと異なるため、呼び出し側で上書きを確認する（V1.2.0）。
/// </summary>
public static class SaveAsPath
{
    private static readonly string[] SupportedExtensions = [".png", ".jpg", ".jpeg", ".jpe"];

    public static bool HasSupportedExtension(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>実際に書くパス。対応している拡張子ならそのまま、それ以外は ".jpg"（JPEG を選んだ場合）か ".png" を付け足す。</summary>
    public static string Complete(string path, bool jpegSelected) =>
        HasSupportedExtension(path) ? path : path + (jpegSelected ? ".jpg" : ".png");
}
