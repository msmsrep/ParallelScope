using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>表示言語の設定が保存・復元され、ツリーの表示名まで追従することの確認。</summary>
[Collection(LanguageCollection.Name)]
public class LanguageSettingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;
    private readonly AppLanguageSetting _originalLanguage = AppLanguage.Current;

    public LanguageSettingTests()
    {
        _fileCacheRepository = new FileCacheRepository(_temp.Path);
        _settingsRepository = new AppSettingsRepository(_temp.Path);
        _settingsRepository.Save(new AppSettings { RootPaths = { @"C:\LanguageTest" } });
    }

    public void Dispose()
    {
        AppLanguage.Apply(_originalLanguage);
        _fileCacheRepository.ReleasePooledConnections();
        _temp.Dispose();
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
    }

    [Fact]
    public void GetLanguage_DefaultsToFollowingWindows()
    {
        Assert.Equal(AppLanguageSetting.System, CreateViewModel().GetLanguage());
    }

    [Fact]
    public void ApplyLanguage_AppliesAndSavesImmediately()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyLanguage(AppLanguageSetting.Japanese);

        Assert.Equal(AppLanguageSetting.Japanese, viewModel.GetLanguage());
        Assert.True(AppLanguage.IsJapanese);
        Assert.Equal(nameof(AppLanguageSetting.Japanese), _settingsRepository.Load().Language);
    }

    [Fact]
    public void ApplyLanguage_RelabelsTheVirtualTreeNodes()
    {
        var viewModel = CreateViewModel();

        viewModel.ApplyLanguage(AppLanguageSetting.English);
        Assert.Equal("Folders", viewModel.AllRootsNode.DisplayName);

        viewModel.ApplyLanguage(AppLanguageSetting.Japanese);
        Assert.Equal("フォルダー", viewModel.AllRootsNode.DisplayName);
    }

    [Fact]
    public void ApplySavedLanguage_RestoresTheSavedLanguageOnStartup()
    {
        CreateViewModel().ApplyLanguage(AppLanguageSetting.Japanese);
        AppLanguage.Apply(AppLanguageSetting.English);

        // 保存済みの設定を読み直したViewModelが、起動時と同じ手順で言語を適用する
        var restarted = CreateViewModel();
        restarted.ApplySavedLanguage();

        Assert.Equal(AppLanguageSetting.Japanese, restarted.GetLanguage());
        Assert.True(AppLanguage.IsJapanese);
    }
}
