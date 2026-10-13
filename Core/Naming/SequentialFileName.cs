using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AreaShot.Core.Localization;

namespace AreaShot.Core.Naming;

/// <summary>説明入力画面などに出す、次に保存する名前の見込み。<see cref="Error"/> があれば保存できない。</summary>
public sealed record SequentialName(int Number, string FileName, string? Error);

/// <summary>
/// 連番のファイル名（V1.3.0。仕様書 §12.2）。"001.png"・"001_説明.png"。
/// 保存先フォルダにある「数字だけ」「数字＋_＋説明」の PNG/JPEG 画像の最大番号＋1 を使う（欠番は埋めない）。
/// </summary>
public static partial class SequentialFileName
{
    /// <summary>連番として数える数字の最大桁数。日時の名前（"261013153000_capture" など）を連番と見なさないため。</summary>
    public const int MaxDigits = 6;
    public const int MaxNumber = 999_999;
    /// <summary>表示の最低桁数（"001"）。</summary>
    public const int MinDigits = 3;
    /// <summary>ファイル名（拡張子を含む）の上限。</summary>
    public const int MaxFileNameLength = 255;
    /// <summary>フルパスの上限（長いパスに対応していないため MAX_PATH − 1）。</summary>
    public const int MaxPathLength = 259;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".jpe"];

    // \d は全角数字などにも一致するため、半角の 0-9 だけを数字とする。
    [GeneratedRegex(@"^([0-9]{1,6})(?:_.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex NamePattern();

    /// <summary>ファイル名（拡張子を含む）が連番の画像なら、その番号を返す。</summary>
    public static int? TryParseNumber(string fileName)
    {
        if (!ImageExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase)) return null;
        var m = NamePattern().Match(Path.GetFileNameWithoutExtension(fileName));
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>ファイル名の一覧から次の番号を求める（連番が無ければ 1）。</summary>
    public static int NextNumber(IEnumerable<string> fileNames)
    {
        var max = 0;
        foreach (var name in fileNames)
        {
            if (TryParseNumber(name) is { } n && n > max) max = n;
        }
        return max + 1;
    }

    /// <summary>
    /// フォルダの中の画像から次の番号を求める。フォルダが無ければ 1。
    /// 読めない場合は IOException / UnauthorizedAccessException などをそのまま返す（呼び出し側でエラーにする）。
    /// </summary>
    public static int NextNumberIn(string directory) =>
        Directory.Exists(directory) ? NextNumber(Directory.EnumerateFiles(directory).Select(Path.GetFileName)!) : 1;

    /// <summary>番号を最低 3 桁で表す（999 の次は 1000）。</summary>
    public static string FormatNumber(int number) =>
        number.ToString("D" + MinDigits, CultureInfo.InvariantCulture);

    /// <summary>
    /// 説明をファイル名に使える形にする。前後の空白を除き、使えない文字（制御文字を含む）を "_" にし、末尾のピリオド・空白を除く。
    /// </summary>
    public static string CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(description.Length);
        foreach (var c in description.Trim()) sb.Append(invalid.Contains(c) || char.IsControl(c) ? '_' : c);
        return sb.ToString().TrimEnd('.', ' ');
    }

    /// <summary>拡張子を含まない名前。説明が空なら番号だけ。</summary>
    public static string BaseName(int number, string? description)
    {
        var clean = CleanDescription(description);
        return clean.Length == 0 ? FormatNumber(number) : FormatNumber(number) + "_" + clean;
    }

    /// <summary>名前が長すぎる・番号が上限を超える場合の誤り。問題が無ければ null。</summary>
    public static string? Validate(string directory, int number, string fileName)
    {
        if (number > MaxNumber) return string.Format(Strings.SequentialNumberExhaustedFormat, FormatNumber(MaxNumber));
        if (fileName.Length > MaxFileNameLength || Path.Combine(directory, fileName).Length > MaxPathLength)
            return Strings.SequentialNameTooLong;
        return null;
    }

    /// <summary>次に保存する名前の見込み。フォルダを読めない場合は例外を返す。</summary>
    public static SequentialName Preview(string directory, string? description, string extension)
    {
        var number = NextNumberIn(directory);
        var fileName = BaseName(number, description) + extension;
        return new SequentialName(number, fileName, Validate(directory, number, fileName));
    }
}
