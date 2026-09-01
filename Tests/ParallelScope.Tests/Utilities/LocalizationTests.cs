using System.Text.RegularExpressions;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.Utilities;

[Collection(LanguageCollection.Name)]
public class LocalizationTests
{
    [Fact]
    public void Resources_HaveTheSameKeysInEveryLanguage()
    {
        Assert.Empty(UiTextResources.English.Keys.Except(UiTextResources.Japanese.Keys));
        Assert.Empty(UiTextResources.Japanese.Keys.Except(UiTextResources.English.Keys));
    }

    [Fact]
    public void Resources_UseTheSamePlaceholdersInEveryLanguage()
    {
        // 訳文でプレースホルダーが抜けたり増えたりすると、UiText.Formatが実行時に落ちる／値が出なくなる
        foreach (var (key, english) in UiTextResources.English)
        {
            Assert.Equal(GetPlaceholders(english), GetPlaceholders(UiTextResources.Japanese[key]));
        }
    }

    [Fact]
    public void Resources_HaveNoEmptyText()
    {
        Assert.All(UiTextResources.English.Values, text => Assert.False(string.IsNullOrWhiteSpace(text)));
        Assert.All(UiTextResources.Japanese.Values, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }

    [Fact]
    public void Get_ReturnsTheTextOfTheCurrentLanguage()
    {
        using (new LanguageScope(AppLanguageSetting.English))
        {
            Assert.Equal("Settings", UiText.Get("Settings.Title"));
        }

        using (new LanguageScope(AppLanguageSetting.Japanese))
        {
            Assert.Equal("設定", UiText.Get("Settings.Title"));
        }
    }

    [Fact]
    public void Get_FallsBackToTheKeyForUnknownKeys()
    {
        using var _ = new LanguageScope(AppLanguageSetting.Japanese);

        Assert.Equal("No.Such.Key", UiText.Get("No.Such.Key"));
    }

    [Fact]
    public void Format_FillsInThePlaceholders()
    {
        using var _ = new LanguageScope(AppLanguageSetting.English);

        Assert.Equal("Full scan failed: disk error", UiText.Format("Scan.Full.Failed", "disk error"));
    }

    [Fact]
    public void Source_RaisesTheIndexerChangeWhenTheLanguageChanges()
    {
        using var _ = new LanguageScope(AppLanguageSetting.English);

        var changedProperties = new List<string?>();
        UiText.Source.PropertyChanged += Handler;
        try
        {
            AppLanguage.Apply(AppLanguageSetting.Japanese);
        }
        finally
        {
            UiText.Source.PropertyChanged -= Handler;
        }

        // "Item[]" は「全てのインデクサーの値が変わった」の意味で、バインディング側が全て再評価する
        Assert.Contains("Item[]", changedProperties);

        void Handler(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);
    }

    [Theory]
    [InlineData("English", AppLanguageSetting.English)]
    [InlineData("japanese", AppLanguageSetting.Japanese)]
    [InlineData("System", AppLanguageSetting.System)]
    [InlineData("", AppLanguageSetting.System)]
    [InlineData(null, AppLanguageSetting.System)]
    [InlineData("Klingon", AppLanguageSetting.System)]
    public void Parse_FallsBackToSystemForUnknownValues(string? value, AppLanguageSetting expected)
    {
        Assert.Equal(expected, AppLanguage.Parse(value));
    }

    [Theory]
    [InlineData(AppLanguageSetting.English, "en")]
    [InlineData(AppLanguageSetting.Japanese, "ja")]
    public void ResolveCulture_MapsTheExplicitLanguages(AppLanguageSetting language, string expectedCultureName)
    {
        Assert.Equal(expectedCultureName, AppLanguage.ResolveCulture(language).Name);
    }

    [Fact]
    public void Apply_SwitchesTheUiCultureSoWpfsOwnResourcesFollow()
    {
        using (new LanguageScope(AppLanguageSetting.Japanese))
        {
            Assert.True(AppLanguage.IsJapanese);
            Assert.Equal("ja", System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }

        using (new LanguageScope(AppLanguageSetting.English))
        {
            Assert.False(AppLanguage.IsJapanese);
            Assert.Equal("en", System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
    }

    private static IReadOnlyList<string> GetPlaceholders(string text)
    {
        return Regex.Matches(text, @"\{\d+\}").Select(match => match.Value).Order(StringComparer.Ordinal).ToList();
    }
}
