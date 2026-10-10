using System.IO;
using System.Text;
using AreaShot.Core.Settings;

namespace AreaShot.Services;

/// <summary>
/// %LocalAppData%\AreaShot\error.log への記録。記録の文言は表示言語によらず英語（V1.1.0〜。一般配布での問い合わせに使うため）。
/// </summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(SettingsStore.DefaultDirectory, "error.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DefaultDirectory);
                var text = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ').AppendLine(message);
                if (exception is not null) text.AppendLine(exception.ToString());
                File.AppendAllText(FilePath, text.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ログが書けなくても本体の動作は続ける。
        }
    }
}
