using System.Text.Json.Serialization;
using AreaShot.Core.Localization;

namespace AreaShot.Core.Input;

/// <summary>範囲選択でマウスを離すときに押しておくキー（仕様書 §5.4）。選べるものは決め打ち。</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReleaseKey>))]
public enum ReleaseKey
{
    None,
    Ctrl,
    Shift,
    CtrlShift,
    Alt,
}

/// <summary>ダイアログを出さずに実行する操作。並びは使う頻度の順。</summary>
public enum QuickAction
{
    Copy,
    Edit,
    Save,
    SaveAs,
}

public static class ReleaseKeys
{
    /// <summary>設定画面の選択肢の順。</summary>
    public static readonly IReadOnlyList<ReleaseKey> All = [ReleaseKey.None, ReleaseKey.Ctrl, ReleaseKey.Shift, ReleaseKey.CtrlShift, ReleaseKey.Alt];

    public static string Describe(ReleaseKey key) => key switch
    {
        ReleaseKey.Ctrl => "Ctrl",
        ReleaseKey.Shift => "Shift",
        ReleaseKey.CtrlShift => "Ctrl+Shift",
        ReleaseKey.Alt => "Alt",
        _ => Strings.ReleaseKeyNone,
    };

    /// <summary>押している修飾キーの組み合わせ。選択肢に無い組み合わせ（Ctrl+Alt、Win を含む等）は None。</summary>
    public static ReleaseKey FromModifiers(KeyModifiers modifiers) => modifiers switch
    {
        KeyModifiers.Control => ReleaseKey.Ctrl,
        KeyModifiers.Shift => ReleaseKey.Shift,
        KeyModifiers.Control | KeyModifiers.Shift => ReleaseKey.CtrlShift,
        KeyModifiers.Alt => ReleaseKey.Alt,
        _ => ReleaseKey.None,
    };
}

/// <summary>「キーを押しながら離すと、ダイアログを出さずに実行する」の設定（仕様書 §5.4・§14）。</summary>
public sealed class QuickActionSettings
{
    public bool Enabled { get; set; } = true;
    public ReleaseKey Copy { get; set; } = ReleaseKey.Ctrl;
    public ReleaseKey Edit { get; set; } = ReleaseKey.Shift;
    public ReleaseKey Save { get; set; } = ReleaseKey.CtrlShift;
    public ReleaseKey SaveAs { get; set; } = ReleaseKey.Alt;

    /// <summary>範囲選択の画面に、割り当てたキーの案内を表示する。</summary>
    public bool ShowHint { get; set; } = true;

    public static string Describe(QuickAction action) => action switch
    {
        QuickAction.Copy => Strings.QuickActionCopy,
        QuickAction.Edit => Strings.QuickActionEdit,
        QuickAction.Save => Strings.QuickActionSave,
        QuickAction.SaveAs => Strings.QuickActionSaveAs,
        _ => action.ToString(),
    };

    /// <summary>範囲選択の画面の案内に使う短い名前。</summary>
    public static string ShortName(QuickAction action) => action switch
    {
        QuickAction.Copy => Strings.QuickShortCopy,
        QuickAction.Edit => Strings.QuickShortEdit,
        QuickAction.Save => Strings.QuickShortSave,
        QuickAction.SaveAs => Strings.QuickShortSaveAs,
        _ => action.ToString(),
    };

    public ReleaseKey KeyOf(QuickAction action) => action switch
    {
        QuickAction.Copy => Copy,
        QuickAction.Edit => Edit,
        QuickAction.Save => Save,
        QuickAction.SaveAs => SaveAs,
        _ => ReleaseKey.None,
    };

    public void SetKey(QuickAction action, ReleaseKey key)
    {
        switch (action)
        {
            case QuickAction.Copy: Copy = key; break;
            case QuickAction.Edit: Edit = key; break;
            case QuickAction.Save: Save = key; break;
            case QuickAction.SaveAs: SaveAs = key; break;
        }
    }

    /// <summary>割り当てのある操作（頻度の順）。無効なら空。</summary>
    public IEnumerable<(QuickAction Action, ReleaseKey Key)> Assigned()
    {
        if (!Enabled) yield break;
        foreach (var action in Enum.GetValues<QuickAction>())
        {
            var key = KeyOf(action);
            if (key != ReleaseKey.None) yield return (action, key);
        }
    }

    /// <summary>離したときに押していたキーに対応する操作。無効・未割当なら null（ダイアログを出す）。</summary>
    public QuickAction? Resolve(ReleaseKey pressed)
    {
        if (pressed == ReleaseKey.None) return null;
        foreach (var (action, key) in Assigned())
        {
            if (key == pressed) return action;
        }
        return null;
    }

    /// <summary>2 つ以上の操作に割り当てられているキー。</summary>
    public IReadOnlyList<ReleaseKey> Duplicates() =>
        Enum.GetValues<QuickAction>().Select(KeyOf).Where(k => k != ReleaseKey.None)
            .GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

    public QuickActionSettings Clone() => (QuickActionSettings)MemberwiseClone();
}
