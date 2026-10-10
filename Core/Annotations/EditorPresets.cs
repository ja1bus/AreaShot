using AreaShot.Core.Localization;

namespace AreaShot.Core.Annotations;

/// <summary>エディタのプリセット値と既定値（仕様書 §9.3）。</summary>
public static class EditorPresets
{
    public static readonly IReadOnlyList<NamedColor> Colors =
    [
        new(nameof(Strings.ColorRed), ArgbColor.FromRgb(0xE6, 0x00, 0x12)),
        new(nameof(Strings.ColorBlack), ArgbColor.FromRgb(0x00, 0x00, 0x00)),
        new(nameof(Strings.ColorBlue), ArgbColor.FromRgb(0x00, 0x5A, 0xFF)),
        new(nameof(Strings.ColorGreen), ArgbColor.FromRgb(0x00, 0xA0, 0x3C)),
        new(nameof(Strings.ColorYellow), ArgbColor.FromRgb(0xFF, 0xD7, 0x00)),
        new(nameof(Strings.ColorWhite), ArgbColor.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    public static readonly IReadOnlyList<double> StrokeWidths = [1, 2, 3, 5, 8];

    public static readonly IReadOnlyList<double> FontSizes = [12, 16, 20, 24, 32, 48];

    public static ArgbColor DefaultColor => Colors[0].Color;
    public const double DefaultStrokeWidth = 3;
    public const double DefaultFontSize = 20;
    public const string DefaultFontFamily = "Meiryo";

    /// <summary>プリセットの中で最も近い値（設定ファイルに範囲外の値が書かれた場合の補正用）。</summary>
    public static double Nearest(IReadOnlyList<double> presets, double value) =>
        double.IsFinite(value) ? presets.MinBy(p => Math.Abs(p - value)) : presets[0];
}

/// <summary>どのスタイル項目を変更できるか（ツールバーの有効・無効）。</summary>
public readonly record struct StyleSupport(bool Color, bool StrokeWidth, bool Font, bool FontSize)
{
    public static readonly StyleSupport None = default;

    /// <summary>
    /// 対象の種類ごとの変更可能項目（仕様書 §9.2）。<paramref name="kind"/> が null（選択ツールで未選択）のときはすべて無効。
    /// </summary>
    public static StyleSupport For(AnnotationKind? kind) => kind switch
    {
        AnnotationKind.Rectangle or AnnotationKind.Arrow or AnnotationKind.Line => new(true, true, false, false),
        AnnotationKind.Text => new(true, false, true, true),
        _ => None,
    };
}
