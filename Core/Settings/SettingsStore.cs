using System.Text;
using System.Text.Json;
using System.Buffers;
using AreaShot.Core.Input;

namespace AreaShot.Core.Settings;

public enum SettingsLoadStatus
{
    Loaded,
    /// <summary>ファイルが無い（初回起動）。既定値を使う。</summary>
    Missing,
    /// <summary>壊れていて読めない。退避して既定値を使う。</summary>
    Corrupt,
}

public sealed record SettingsLoadResult(AppSettings Settings, SettingsLoadStatus Status, IReadOnlyList<string> Fixes, string? BackupPath = null, string? Error = null);

/// <summary>
/// 設定ファイル（UTF-8 JSON）の読み書き（仕様書 §14.2）。
/// 壊れたファイルは settings.json.broken-yyyyMMddHHmmss へ退避してから既定値で続行する。
/// 書き込みは一時ファイル経由で置き換え、途中で失敗しても元のファイルを壊さない。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreaShot");

    public static SettingsStore CreateDefault() => new(Path.Combine(DefaultDirectory, "settings.json"));

    public string FilePath { get; }

    public SettingsLoadResult Load()
    {
        if (!File.Exists(FilePath))
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Missing, []);

        try
        {
            var json = File.ReadAllText(FilePath, Encoding.UTF8);
            // 読めない列挙値は、その項目だけ既定値にする（V1.4.0。設定全体を既定値に戻さない）。
            var (repaired, enumFixes) = RepairEnums(json);
            var fixes = new List<string>(enumFixes);
            var settings = JsonSerializer.Deserialize<AppSettings>(repaired, JsonOptions)
                           ?? throw new JsonException("The settings file is empty.");
            fixes.AddRange(SettingsNormalizer.Normalize(settings));
            return new SettingsLoadResult(settings, SettingsLoadStatus.Loaded, fixes);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            string? backup = null;
            try
            {
                backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(FilePath, backup, overwrite: true);
            }
            catch (Exception copyError) when (copyError is IOException or UnauthorizedAccessException)
            {
                backup = null;
            }
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, [], backup, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 読めない（ロック・権限）。既定値で続行するが、退避はしない（ファイルは壊れていない可能性がある）。
            return new SettingsLoadResult(new AppSettings(), SettingsLoadStatus.Corrupt, [], null, ex.Message);
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>列挙値の項目。読めるかどうかは、デシリアライズと同じ変換器（JsonStringEnumConverter）で判定する。</summary>
    private sealed record EnumRule(string Name, Func<JsonElement, bool> CanRead, string Fallback);

    private static EnumRule Rule<T>(string name, T fallback) where T : struct, Enum =>
        new(name, CanRead<T>, fallback.ToString());

    private static readonly EnumRule[] RootRules =
    [
        Rule(nameof(AppSettings.ImageFormat), new AppSettings().ImageFormat),
        Rule(nameof(AppSettings.NamingMode), new AppSettings().NamingMode),
    ];

    private static readonly EnumRule[] QuickActionRules =
        Enum.GetValues<QuickAction>().Select(a => Rule(a.ToString(), new QuickActionSettings().KeyOf(a))).ToArray();

    /// <summary>
    /// 列挙値のプロパティ（ImageFormat・NamingMode・QuickActions の各キー）で、従来の変換で読めない値を取り除いた JSON と、
    /// 直した項目を返す（取り除いた項目は既定値で読まれる）。
    /// <para>
    /// 名前は大文字・小文字を区別しない。同じ名前が重なっている場合は、従来どおり最後の指定が有効で、最後の指定が読めなければ
    /// その項目のすべての指定を取り除く（最後の指定が読めれば、前の読めない指定だけを黙って取り除く）。
    /// 重複キーを保てるよう <see cref="JsonDocument"/> で読み、他のプロパティはそのまま書き戻す。
    /// 範囲外の数値は <see cref="SettingsNormalizer"/> が直す。JSON として読めない・オブジェクトでない場合は <see cref="JsonException"/>。
    /// </para>
    /// </summary>
    internal static (byte[] Json, IReadOnlyList<string> Fixes) RepairEnums(string json)
    {
        using var doc = JsonDocument.Parse(json, DocumentOptions);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The settings file does not contain an object.");

        var fixes = new List<string>();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteRepaired(writer, doc.RootElement, RootRules, "", record: true, fixes, isRoot: true);
        }
        return (buffer.WrittenSpan.ToArray(), fixes);
    }

    private static void WriteRepaired(Utf8JsonWriter writer, JsonElement obj, EnumRule[] rules, string prefix, bool record,
        List<string> fixes, bool isRoot)
    {
        var props = obj.EnumerateObject().ToList();

        // 各項目の最後の指定（有効になる値）が読めるか。
        var lastReadable = new Dictionary<EnumRule, bool>();
        foreach (var rule in rules)
        {
            var last = props.FindLastIndex(p => SameName(p.Name, rule.Name));
            if (last < 0) continue;
            var value = props[last].Value;
            var ok = rule.CanRead(value);
            lastReadable[rule] = ok;
            if (!ok && record) fixes.Add($"{prefix}{rule.Name}: '{Shown(value)}' → {rule.Fallback}");
        }

        var lastQuick = isRoot ? props.FindLastIndex(p => SameName(p.Name, nameof(AppSettings.QuickActions))) : -1;

        writer.WriteStartObject();
        for (var i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var rule = rules.FirstOrDefault(r => SameName(p.Name, r.Name));
            if (rule is not null)
            {
                if (lastReadable[rule] && rule.CanRead(p.Value)) p.WriteTo(writer);
                continue;
            }
            if (isRoot && SameName(p.Name, nameof(AppSettings.QuickActions)) && p.Value.ValueKind == JsonValueKind.Object)
            {
                // 有効になるのは最後の QuickActions だけなので、補正の記録もそれだけにする。
                writer.WritePropertyName(p.Name);
                WriteRepaired(writer, p.Value, QuickActionRules, nameof(AppSettings.QuickActions) + ".", record && i == lastQuick,
                    fixes, isRoot: false);
                continue;
            }
            p.WriteTo(writer);
        }
        writer.WriteEndObject();
    }

    private static bool CanRead<T>(JsonElement value) where T : struct, Enum
    {
        try
        {
            JsonSerializer.Deserialize<T>(value, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Shown(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();

    /// <summary>保存する。失敗時は例外（IOException / UnauthorizedAccessException）を呼び出し側へ返す。</summary>
    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json, new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }
}
