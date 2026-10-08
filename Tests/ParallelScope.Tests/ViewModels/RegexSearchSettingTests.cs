using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// 正規表現検索の設定（Plus機能）の確認。設定値の保存と、購読状態による有効/無効の切り替わり方を見る。
/// 照合そのものは <see cref="Utilities.NameSearchPatternTests"/> で確かめている。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class RegexSearchSettingTests : ShellTestBase
{
    private readonly TempDirectory _root;

    public RegexSearchSettingTests()
    {
        _root = NewTempDirectory();
        SettingsRepository.Save(new AppSettings { RootPaths = { _root.Path } });
    }

    [Fact]
    public void DefaultsToDisabled()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.GetRegexSearchEnabled());
        Assert.False(viewModel.IsRegexSearchActive);
    }

    [Fact]
    public void SettingIsSavedAndRestored()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        viewModel.SetRegexSearchEnabled(true);

        Assert.True(SettingsRepository.Load().IsRegexSearchEnabled);
        Assert.True(CreateViewModel().GetRegexSearchEnabled());
    }

    // Plus機能なので、設定が有効でも購読が確認できるまでは従来の部分一致で検索する
    [Fact]
    public void IsNotAppliedWhilePlusIsInactive()
    {
        var viewModel = CreateViewModel();
        viewModel.SetRegexSearchEnabled(true);

        Assert.True(viewModel.GetRegexSearchEnabled());
        Assert.False(viewModel.IsRegexSearchActive);
    }

    [Fact]
    public void IsAppliedOnceEnabledAndSubscribed()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        viewModel.SetRegexSearchEnabled(true);

        Assert.True(viewModel.IsRegexSearchActive);
    }

    // 購読が切れたら部分一致へ戻す（設定自体は残す）
    [Fact]
    public void FallsBackToSubstringWhenPlusLapses()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetRegexSearchEnabled(true);

        viewModel.SetPlusFeaturesEnabled(false);

        Assert.False(viewModel.IsRegexSearchActive);
        Assert.True(viewModel.GetRegexSearchEnabled());
    }

    // 打ちかけで式が成立しない間は一覧を据え置き、検索欄を赤くするだけにする（1文字ごとに一覧を消さないため）
    [Fact]
    public void BrokenPatternKeepsTheListAndFlagsTheQuery()
    {
        var sub = Path.Combine(_root.Path, "Sub");
        FileCacheRepository.ReplaceEntriesByParentPath(sub, new[]
        {
            CacheEntries.File(sub, "alpha.txt"),
            CacheEntries.File(sub, "beta.txt")
        });
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetRegexSearchEnabled(true);
        var tab = viewModel.ActiveTab;

        tab.SearchQuery = "^a";
        Wait.ForItemNames(tab, "alpha.txt");

        tab.SearchQuery = "^a(";
        Assert.True(tab.IsSearchQueryInvalid);
        Assert.Equal(new[] { "alpha.txt" }, tab.FileItems.Select(x => x.Name));

        tab.SearchQuery = "^b";
        Assert.False(tab.IsSearchQueryInvalid);
        Wait.ForItemNames(tab, "beta.txt");
    }
}
