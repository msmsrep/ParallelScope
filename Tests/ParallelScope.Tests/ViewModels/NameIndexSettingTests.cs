using System.Diagnostics;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// ファイル名索引の設定（Plus機能）の確認。設定値の保存と、購読状態による有効/無効の切り替わり方を見る。
/// 索引そのものの検索結果は <see cref="Data.FileNameIndexTests"/> で確かめている。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class NameIndexSettingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _root = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public NameIndexSettingTests()
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

    /// <summary>索引の組み立てはバックグラウンドで走るため、出来上がるまで待つ。</summary>
    private static void WaitForIndexReady(MainWindowViewModel viewModel, bool expected)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (viewModel.IsNameIndexReady == expected)
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail($"索引の状態が {expected} になりませんでした");
    }

    [Fact]
    public void DefaultsToDisabled()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.GetNameIndexEnabled());
        Assert.False(viewModel.IsNameIndexReady);
    }

    [Fact]
    public void SettingIsSavedAndRestored()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        viewModel.SetNameIndexEnabled(true);

        Assert.True(_settingsRepository.Load().IsNameIndexEnabled);
        Assert.True(CreateViewModel().GetNameIndexEnabled());
    }

    // 索引はPlus機能。設定が有効でも、購読が確認できるまでは組み立てない
    [Fact]
    public void IndexIsNotBuiltWhilePlusIsInactive()
    {
        var viewModel = CreateViewModel();
        viewModel.SetNameIndexEnabled(true);

        Assert.True(viewModel.GetNameIndexEnabled());
        Assert.False(viewModel.IsNameIndexReady);
    }

    [Fact]
    public void IndexIsBuiltOnceEnabledAndSubscribed()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);

        viewModel.SetNameIndexEnabled(true);

        WaitForIndexReady(viewModel, true);
    }

    // 購読が切れたら索引を捨ててメモリを返す（設定自体は残す）
    [Fact]
    public void IndexIsDroppedWhenPlusLapses()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetNameIndexEnabled(true);
        WaitForIndexReady(viewModel, true);

        viewModel.SetPlusFeaturesEnabled(false);

        Assert.False(viewModel.IsNameIndexReady);
        Assert.True(viewModel.GetNameIndexEnabled());
    }

    [Fact]
    public void IndexIsDroppedWhenTurnedOff()
    {
        var viewModel = CreateViewModel();
        viewModel.SetPlusFeaturesEnabled(true);
        viewModel.SetNameIndexEnabled(true);
        WaitForIndexReady(viewModel, true);

        viewModel.SetNameIndexEnabled(false);

        Assert.False(viewModel.IsNameIndexReady);
        Assert.False(_settingsRepository.Load().IsNameIndexEnabled);
    }
}
