using System.Security.Cryptography;

namespace AreaShot.Core.Output;

/// <summary>
/// 画像ファイルを、保存先と同じフォルダーの一時ファイルに書いてから保存先へ移す（仕様書 §12.3。V1.2.0）。
/// 書き込みの途中で失敗しても（容量不足・接続の切断など）既存のファイルは元のまま残り、書きかけのファイルも残さない。
/// </summary>
public static class SafeFileWriter
{
    /// <summary>一時ファイル名の接頭辞。拡張子 .tmp は暗号化される拡張子（local/C# 規約）に含まれない。</summary>
    public const string TempPrefix = "~AreaShot-";
    public const string TempExtension = ".tmp";

    /// <summary><paramref name="path"/> に書く。既存のファイルがあれば、書き終えてから置き換える。</summary>
    public static void WriteReplacing(string path, Action<Stream> write)
    {
        var temp = WriteTemp(Path.GetDirectoryName(Path.GetFullPath(path))!, write);
        try
        {
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>
    /// 既存のファイルを上書きせずに新しく作る。<paramref name="choosePath"/> は、その時点で存在しないパスを返す。
    /// 書き終えてから移す間に同名のファイルができた場合だけ、選び直して移し直す（書き直しはしない）。作ったパスを返す。
    /// </summary>
    public static string WriteNew(string directory, Func<string> choosePath, Action<Stream> write, int maxAttempts = 5)
    {
        var temp = WriteTemp(directory, write);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var path = choosePath();
                try
                {
                    File.Move(temp, path, overwrite: false);
                    return path;
                }
                catch (IOException) when (File.Exists(path) && attempt < maxAttempts)
                {
                    // 同名のファイルが直前に作られた。次の名前で移し直す。
                }
            }
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>一時ファイルを新しく作って書き、ディスクまで書き出す。失敗したら一時ファイルを消して例外を投げ直す。</summary>
    private static string WriteTemp(string directory, Action<Stream> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            var temp = Path.Combine(directory, TempPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)) + TempExtension);
            FileStream stream;
            try
            {
                stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (File.Exists(temp) && attempt < 5)
            {
                continue; // 乱数の名前が重なった（通常は起きない）。
            }
            try
            {
                using (stream)
                {
                    write(stream);
                    stream.Flush(flushToDisk: true);
                }
                return temp;
            }
            catch
            {
                DeleteQuietly(temp);
                throw;
            }
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 消せなくても保存の結果は変わらない（一時ファイルが残るだけ）。
        }
    }
}
