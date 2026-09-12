using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// 正規表現検索の設定（Plus機能）の確認。設定値の保存と、購読状態による有効/無効の切り替わり方を見る。
/// 照合そのものは <see cref="Utilities.NameSearchPatternTests"/> で確かめている。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class RegexSearchSettingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _root = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public RegexSearchSettingTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
        _settingsRepository.Save(new AppSettings { RootPaths = { _root.Path } });
    }

    public void Dispose()
    {
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
        _root.Dispose();
    }

    private MainWindowViewModel CreateViewModel() => new(_fileCacheRepository, _settingsRepository);

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

        Assert.True(_settingsRepository.Load().IsRegexSearchEnabled);
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
}
