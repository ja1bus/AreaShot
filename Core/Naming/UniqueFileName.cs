using AreaShot.Core.Localization;

namespace AreaShot.Core.Naming;

/// <summary>
/// 同名ファイルがある場合だけ連番を付ける（仕様書 §12.2）。
/// "260922224012_capture.png" → "260922224012_capture_2.png" → "_3" …
/// </summary>
public static class UniqueFileName
{
    /// <summary>連番の上限（これを超えることは通常ない。無限ループ防止）。</summary>
    public const int MaxSuffix = 9999;

    /// <summary>
    /// <paramref name="directory"/> に <paramref name="baseName"/> + <paramref name="extension"/> を作るときの、
    /// 既存と重ならないフルパスを返す。<paramref name="exists"/> はファイルの有無の判定（テストで差し替える）。
    /// </summary>
    public static string Resolve(string directory, string baseName, string extension, Func<string, bool> exists)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        var first = Path.Combine(directory, baseName + extension);
        if (!exists(first)) return first;
        for (var n = 2; n <= MaxSuffix; n++)
        {
            var candidate = Path.Combine(directory, $"{baseName}_{n}{extension}");
            if (!exists(candidate)) return candidate;
        }
        throw new IOException(string.Format(Strings.UniqueNameExhaustedFormat, first));
    }
}
