using System.Globalization;

namespace ParallelScope.Utilities;

/// <summary>アプリの表示言語設定。settings.jsonには名前（"System"/"English"/"Japanese"）で保存する。</summary>
public enum AppLanguageSetting
{
    /// <summary>Windowsの表示言語に追従する（日本語環境なら日本語、それ以外は英語）。</summary>
    System,
    English,
    Japanese
}

/// <summary>表示言語設定の解釈とアプリ全体への適用。</summary>
public static class AppLanguage
{
    // Windowsの表示言語。AppLanguage.Applyが CurrentUICulture を上書きするため、
    // 上書き前の値（＝OSの設定）をクラス初期化時に控えておく
    private static readonly bool SystemPrefersJapanese =
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);

    /// <summary>言語が切り替わったときに発生する（表示中の文字列を組み立て直すために使う）。</summary>
    public static event EventHandler? Changed;

    /// <summary>現在の表示言語設定（Systemは解決前の値のまま）。</summary>
    public static AppLanguageSetting Current { get; private set; } = AppLanguageSetting.System;

    /// <summary>実際に表示へ使う言語が日本語か（Systemを解決した結果）。</summary>
    public static bool IsJapanese { get; private set; } = SystemPrefersJapanese;

    /// <summary>設定ファイルの文字列を言語設定へ変換する。未設定・不正値はOS追従に丸める。</summary>
    public static AppLanguageSetting Parse(string? value)
    {
        return Enum.TryParse<AppLanguageSetting>(value, true, out var parsed) ? parsed : AppLanguageSetting.System;
    }

    /// <summary>言語設定を実際のカルチャへ解決する。</summary>
    public static CultureInfo ResolveCulture(AppLanguageSetting setting)
    {
        return CultureInfo.GetCultureInfo(IsJapaneseFor(setting) ? "ja" : "en");
    }

    /// <summary>指定言語をアプリ全体へ適用する（既に同じ言語なら何もしない）。</summary>
    public static void Apply(AppLanguageSetting setting)
    {
        var isJapanese = IsJapaneseFor(setting);
        if (Current == setting && IsJapanese == isJapanese)
        {
            return;
        }

        Current = setting;
        IsJapanese = isJapanese;

        // WPF自身のリソース（TextBoxの右クリックメニュー等）もこのカルチャに追従する。
        // csprojのSatelliteResourceLanguagesでen/jaのサテライトアセンブリを残しているのはこのため
        var culture = CultureInfo.GetCultureInfo(isJapanese ? "ja" : "en");
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // バインディング経由の文字列を貼り替えてから、コードで組み立てている文字列の更新を通知する
        UiText.NotifyLanguageChanged();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsJapaneseFor(AppLanguageSetting setting) => setting switch
    {
        AppLanguageSetting.Japanese => true,
        AppLanguageSetting.English => false,
        _ => SystemPrefersJapanese
    };
}
