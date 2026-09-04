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
        WaitFor(tab, () => tab.FileItems.Count == expectedCount, $"{expectedCount} 件になる");
    }

    /// <summary>件数が変わらない場合（1件→別の1件）は件数待ちでは素通りしてしまうため、中身で待つ。</summary>
    private static void WaitForSingleItemNamed(BrowserTabViewModel tab, string expectedName)
    {
        WaitFor(
            tab,
            () => tab.FileItems.Count == 1 && tab.FileItems[0].Name == expectedName,
            $"{expectedName} の1件だけになる");
    }

    /// <summary>取得はバックグラウンドで進むため、条件が満たされるまで待つ（満たされなければ失敗させる）。</summary>
    private static void WaitFor(BrowserTabViewModel tab, Func<bool> condition, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(20);
        }

        var names = string.Join(", ", tab.FileItems.Take(5).Select(x => x.Name));
        Assert.Fail($"一覧が「{description}」になりませんでした（実際は {tab.FileItems.Count} 件: {names}）");
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

    // 全件そろうのを待たず、貯まった分から順に出す（All Filesの段階表示と同じ）
    [Fact]
    public void Search_ShowsPartialResultsBeforeTheWholeSearchFinishes()
    {
        const int fileCount = 17_000;
        SeedCachedFiles(fileCount);
        var tab = CreateTabAtRoot();

        // 一覧が差し替わるたびの件数を控える
        var observedCounts = new List<int>();
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BrowserTabViewModel.FileItems))
            {
                lock (observedCounts)
                {
                    observedCounts.Add(tab.FileItems.Count);
                }
            }
        };

        tab.SearchQuery = "file";

        WaitForItemCount(tab, fileCount);

        lock (observedCounts)
        {
            // 最初の2,000件と、その8倍の16,000件の時点で表示されている
            Assert.Contains(2_000, observedCounts);
            Assert.Contains(16_000, observedCounts);
        }
    }

    // 途中経過から絞り込むと結果が欠けるため、絞り込みに使ってよいのは全件そろった結果だけ
    [Fact]
    public void Search_DoesNotNarrowFromAPartialResult()
    {
        const int fileCount = 17_000;
        SeedCachedFiles(fileCount);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file";
        WaitForItemCount(tab, fileCount);

        // "file" の全件（17,000件）から絞り込める。途中経過（2,000件など）が控えられていると取りこぼす。
        // file016000〜file016099 の100件が該当する
        tab.SearchQuery = "file0160";

        WaitForItemCount(tab, 100);
        Assert.All(tab.FileItems, item => Assert.StartsWith("file0160", item.Name));
    }

    // 検索語を足した場合、新しい結果は必ず前回の結果の部分集合になるので、
    // キャッシュDBを引き直さずに前回の結果から絞り込む。結果が引き直した場合と同じであること
    [Fact]
    public void Search_NarrowsFromThePreviousResultWhenTheQueryIsExtended()
    {
        SeedCachedFiles(10);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file00000";
        WaitForItemCount(tab, 10);

        tab.SearchQuery = "file000004";

        WaitForSingleItemNamed(tab, "file000004.txt");
    }

    // 前回の結果に無い行が必要になる検索語（打ち足しでない）は、絞り込みで済ませてはいけない
    [Fact]
    public void Search_QueriesAgainWhenTheNewQueryIsNotAnExtension()
    {
        SeedCachedFiles(10);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file000004";
        WaitForItemCount(tab, 1);

        // 前回の結果（1件）には含まれない行が対象になる
        tab.SearchQuery = "file000007";

        WaitForSingleItemNamed(tab, "file000007.txt");
    }

    // フォルダを移動したら、前のフォルダの検索結果から絞り込んではいけない
    [Fact]
    public void Search_DoesNotReusePreviousResultsAfterMovingToAnotherFolder()
    {
        SeedCachedFiles(10);
        var otherFolderPath = Path.Combine(_root.Path, "Other");
        Directory.CreateDirectory(otherFolderPath);
        // 検索はキャッシュだけを見るので行を入れておき、移動先のライブ読み込みで
        // 上書きされても消えないよう実体も置く（どちらが先でも1件になる）
        File.WriteAllText(Path.Combine(otherFolderPath, "file000004.txt"), "x");
        _fileCacheRepository.ReplaceEntriesByParentPath(otherFolderPath, new[]
        {
            new CachedFileSystemEntry(
                otherFolderPath,
                Path.Combine(otherFolderPath, "file000004.txt"),
                "file000004.txt",
                IsFolder: false,
                SizeBytes: 1,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                CreationTimeUtc: null,
                Attributes: 32)
        });

        var tab = CreateTabAtRoot();
        // ルートからの検索は Sub の10件と Other の1件（同名の file000004.txt）を拾う
        tab.SearchQuery = "file00000";
        WaitForItemCount(tab, 11);

        Assert.True(tab.NavigateTo(otherFolderPath, addToHistory: false));
        tab.SearchQuery = "file000004";

        WaitForSingleItemNamed(tab, "file000004.txt");
        Assert.Equal(otherFolderPath, tab.FileItems[0].Location);
    }

    [Fact]
    public void Search_RestoresTheDirectoryListingWhenCleared()
    {
        SeedCachedFiles(10);
        var tab = CreateTabAtRoot();

        tab.SearchQuery = "file000003";
        WaitForItemCount(tab, 1);

        tab.SearchQuery = string.Empty;

        // 検索前の直下一覧（Sub フォルダのみ）に戻る。件数が同じ1件なので中身で待つ
        WaitForSingleItemNamed(tab, "Sub");
        Assert.True(tab.FileItems[0].IsFolder);
    }
}
