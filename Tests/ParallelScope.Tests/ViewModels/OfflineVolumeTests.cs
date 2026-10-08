using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// ボリュームごと見えない（切断中のNAS等）フォルダを、キャッシュの内容で開けることの確認。
/// 見えないボリュームの代わりに未割り当てのドライブ文字を使う。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class OfflineVolumeTests : ShellTestBase
{
    private readonly string _offlineParent = Path.Combine(UnusedDrive.Root, "Share");
    private readonly string _offlineFolder;

    public OfflineVolumeTests()
    {
        _offlineFolder = Path.Combine(_offlineParent, "Cached");
        SettingsRepository.Save(new AppSettings { RootPaths = { NewTempDirectory().Path } });
        FileCacheRepository.ReplaceEntriesByParentPath(_offlineParent, new[] { CacheEntries.Folder(_offlineParent, "Cached") });
        FileCacheRepository.ReplaceEntriesByParentPath(_offlineFolder, new[] { CacheEntries.File(_offlineFolder, "a.txt") });
    }

    [Fact]
    public void CachedFolderOnAnUnreachableVolume_OpensFromTheCache()
    {
        var tab = CreateViewModel().ActiveTab;

        Assert.True(tab.LoadFiles(_offlineFolder));

        Assert.Equal(_offlineFolder, tab.CurrentPath);
        Wait.ForItemNames(tab, "a.txt");
    }

    // 復帰したボリュームを表示中のタブは読み直す（切断中はファイルシステムからの最新化を省いていたため）
    [Fact]
    public void RefreshAfterVolumeRestored_ReloadsTabsOnThatVolume()
    {
        var viewModel = CreateViewModel();
        var tab = viewModel.ActiveTab;
        tab.LoadFiles(_offlineFolder);
        Wait.ForItemNames(tab, "a.txt");

        FileCacheRepository.ReplaceEntriesByParentPath(_offlineFolder, new[]
        {
            CacheEntries.File(_offlineFolder, "a.txt"),
            CacheEntries.File(_offlineFolder, "b.txt")
        });
        viewModel.RefreshAfterVolumeRestored(UnusedDrive.Root);

        Wait.ForItemNames(tab, "a.txt", "b.txt");
    }
}
