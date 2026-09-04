using System.Diagnostics;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// キャッシュに対する検索（インクリメンタルサーチ）の確認。
/// 検索語は入力の都度リクエストされ、古い検索は途中で打ち切られる。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class SearchTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _root = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public SearchTests()
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

    /// <summary>打ち切りの確認間隔（1,000件）を何度も跨ぐよう、多めのファイルをキャッシュへ入れる。</summary>
    private void SeedCachedFiles(int fileCount)
    {
        var subFolderPath = Path.Combine(_root.Path, "Sub");
        Directory.CreateDirectory(subFolderPath);

        var entries = Enumerable.Range(0, fileCount)
            .Select(i => new CachedFileSystemEntry(
                subFolderPath,
                Path.Combine(subFolderPath, $"file{i:D6}.txt"),
                $"file{i:D6}.txt",
                IsFolder: false,
                SizeBytes: i,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                CreationTimeUtc: null,
                Attributes: 32))
            .ToList();

        _fileCacheRepository.ReplaceEntriesByParentPath(subFolderPath, entries);
    }

    private static void WaitForItemCount(BrowserTabViewModel tab, int expectedCount)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (tab.FileItems.Count == expectedCount)
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail($"一覧が {expectedCount} 件になりませんでした（実際は {tab.FileItems.Count} 件）");
    }

    private BrowserTabViewModel CreateTabAtRoot()
    {
        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));
        return tab;
    }

    [Fact]
    public void Search_ShowsCachedMatchesUnderTheCurrentFolder()
    {
        SeedCachedFiles(10);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file000003";

        WaitForItemCount(tab, 1);
        Assert.Equal("file000003.txt", tab.FileItems[0].Name);
    }

    // 前の検索語の列挙が残っていると、次の入力の検索がその後ろで待たされる（キューは直列実行）。
    // 打ち切られた結果が後から画面へ流れ込まないことも合わせて見る
    [Fact]
    public void Search_LatestQueryWinsWhenTypedBeforeThePreviousOneFinishes()
    {
        SeedCachedFiles(17_000);
        var tab = CreateTabAtRoot();

        // 全件ヒットする検索語で列挙を始めさせ、終わる前に絞り込む
        tab.SearchQuery = "file";
        tab.SearchQuery = "file000042";

        WaitForItemCount(tab, 1);
        Assert.Equal("file000042.txt", tab.FileItems[0].Name);

        Thread.Sleep(500);
        Assert.Single(tab.FileItems);
        Assert.Equal("file000042.txt", tab.FileItems[0].Name);
    }

    [Fact]
    public void Search_RestoresTheDirectoryListingWhenCleared()
    {
        SeedCachedFiles(10);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file000003";
        WaitForItemCount(tab, 1);

        tab.SearchQuery = string.Empty;

        // 検索前の直下一覧（Sub フォルダのみ）に戻る
        WaitForItemCount(tab, 1);
        Assert.Equal("Sub", tab.FileItems[0].Name);
        Assert.True(tab.FileItems[0].IsFolder);
    }
}
