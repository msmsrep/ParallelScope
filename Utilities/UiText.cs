using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace ParallelScope.Utilities;

/// <summary>
/// UI表示文字列の取得口。対訳表は<see cref="UiTextResources"/>にあり、
/// 現在の言語（<see cref="AppLanguage"/>）に応じて引き当てる。
/// </summary>
public static class UiText
{
    /// <summary>XAMLのバインディング元。言語切り替え時にインデクサーの変更通知を出す。</summary>
    public static LocalizedTextSource Source { get; } = new();

    /// <summary>キーに対応する表示文字列を返す。日本語側に無いキーは英語へ、未知のキーはキー自体へフォールバックする。</summary>
    public static string Get(string key)
    {
        if (AppLanguage.IsJapanese && UiTextResources.Japanese.TryGetValue(key, out var japanese))
        {
            return japanese;
        }

        return UiTextResources.English.TryGetValue(key, out var english) ? english : key;
    }

    /// <summary>プレースホルダー（{0}など）を持つ表示文字列を組み立てる。</summary>
    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    /// <summary>言語切り替えをバインディングへ通知する（<see cref="AppLanguage.Apply"/>から呼ばれる）。</summary>
    internal static void NotifyLanguageChanged()
    {
        Source.RaiseAllChanged();
    }
}

/// <summary>
/// XAMLからキーで表示文字列を引くためのバインディング元。
/// 言語切り替え時にインデクサーの変更通知を出すことで、画面上の文字列がその場で貼り替わる。
/// </summary>
public sealed class LocalizedTextSource : INotifyPropertyChanged
{
    internal LocalizedTextSource()
    {
    }

    public string this[string key] => UiText.Get(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void RaiseAllChanged()
    {
        // Binding.IndexerName（"Item[]"）は「全てのインデクサーの値が変わった」を意味する
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }
}

/// <summary>
/// XAMLで表示文字列を指定するマークアップ拡張（<c>{loc:Loc Menu.Settings}</c>）。
/// 定数ではなくバインディングを返すため、言語を切り替えるとその場で文字列が入れ替わる。
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>対訳表のキー（<see cref="UiTextResources"/>）。</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = UiText.Source,
            Mode = BindingMode.OneWay
        };

        // Setter等ではBinding自身を、通常のプロパティではBindingExpressionを返す必要があるため、Binding側に委ねる
        return binding.ProvideValue(serviceProvider);
    }
}
