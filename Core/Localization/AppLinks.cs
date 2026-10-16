using System.Globalization;

namespace AreaShot.Core.Localization;

/// <summary>
/// Web の案内ページ（仕様書 §14.5）。取扱説明書はアプリに入れず Web に置き、設定画面から開く。
/// URL はサイト（cloudflare/www/public/areashot/）の構成に合わせる。変えるときはここだけ直す。
/// </summary>
public static class AppLinks
{
    public const string ManualJapanese = "https://www.ja1bus.com/areashot/manual/";
    public const string ManualEnglish = "https://www.ja1bus.com/areashot/en/manual/";
    /// <summary>ソースを MIT License で公開している公開リポジトリ（V1.2.0。設定画面の「ソースコード」）。</summary>
    public const string Repository = "https://github.com/ja1bus/AreaShot";

    /// <summary>表示言語（実際に使っている言語）に合わせた取扱説明書の URL。日本語以外は英語。</summary>
    public static string Manual(CultureInfo uiCulture) =>
        uiCulture.TwoLetterISOLanguageName == UiLanguage.Japanese ? ManualJapanese : ManualEnglish;
}
