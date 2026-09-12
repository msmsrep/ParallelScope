using System.Diagnostics;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// All Filesモード（フラット表示）の取得結果の確認。取得は「貯まった分から順に出す」段階表示のため、
/// 途中経過を挟んでも最終的に全件そろうことを見る。
/// </summary>
[Collection(FolderTreeCollection.Name)]
public class FlatFileViewTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly TempDirectory _root = new();
    private readonly FileCacheRepository _fileCacheRepository;
    private readonly AppSettingsRepository _settingsRepository;

    public FlatFileViewTests()
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

    /// <summary>途中経過が複数回入るよう、最初の表示件数（2,000）を超える数のファイルをキャッシュへ入れる。</summary>
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

    /// <summary>取得はバックグラウンドで進むため、期待件数に届くまで待つ（届かなければ失敗させる）。</summary>
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

    [Fact]
    public void FlatFileView_EventuallyShowsEveryCachedFileUnderTheFolder()
    {
        // 最初の表示（2,000件）と、その後の間隔（×8）を超えて途中経過が複数回走る件数
        const int fileCount = 17_000;
        SeedCachedFiles(fileCount);

        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));

        // 一覧が差し替わるたびの件数を控える（段階表示なら全件そろう前の件数が現れる）
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

        tab.IsFlatFileViewEnabled = true;

        WaitForItemCount(tab, fileCount);
        Assert.All(tab.FileItems, item => Assert.False(item.IsFolder));
        // 途中経過の複製で同じアイテムが二重に載っていないこと
        Assert.Equal(fileCount, tab.FileItems.Select(x => x.Name).Distinct().Count());

        lock (observedCounts)
        {
            // 最初の2,000件と、その8倍の16,000件の時点で表示されている
            Assert.Contains(2_000, observedCounts);
            Assert.Contains(16_000, observedCounts);
        }
    }

    // 取得の途中でフォルダを移動された場合、その取得はそこで打ち切って移動先に譲る
    // （フラット表示のキューは直列実行のため、打ち切らないと移動先の取得が後ろで待たされる）
    [Fact]
    public void FlatFileView_AbandonsTheLoadWhenTheFolderChangesMidway()
    {
        const int largeFileCount = 17_000;
        SeedCachedFiles(largeFileCount);

        var otherFolderPath = Path.Combine(_root.Path, "Other");
        Directory.CreateDirectory(otherFolderPath);
        // 移動先はライブのファイルシステムも読み直されてキャッシュが上書きされるため、実体も置く
        // （キャッシュだけ入れておくと、上書きで消えて空一覧になる）
        File.WriteAllText(Path.Combine(otherFolderPath, "only.txt"), "x");
        _fileCacheRepository.ReplaceEntriesByParentPath(otherFolderPath, new[]
        {
            new CachedFileSystemEntry(
                otherFolderPath,
                Path.Combine(otherFolderPath, "only.txt"),
                "only.txt",
                IsFolder: false,
                SizeBytes: 1,
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                CreationTimeUtc: null,
                Attributes: 32)
        });

        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));

        tab.IsFlatFileViewEnabled = true;
        // ルート配下の取得が終わる前に移動する
        Assert.True(tab.NavigateTo(otherFolderPath, addToHistory: false));

        WaitForItemCount(tab, 1);
        Assert.Equal("only.txt", tab.FileItems[0].Name);

        // 打ち切ったはずの取得が後から結果を流し込んでこないこと
        Thread.Sleep(500);
        Assert.Single(tab.FileItems);
        Assert.Equal("only.txt", tab.FileItems[0].Name);
    }

    [Fact]
    public void FlatFileView_ShowsAllFilesWhenFewerThanTheFirstBatch()
    {
        const int fileCount = 10;
        SeedCachedFiles(fileCount);

        var viewModel = new MainWindowViewModel(_fileCacheRepository, _settingsRepository);
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));

        tab.IsFlatFileViewEnabled = true;

        WaitForItemCount(tab, fileCount);
    }
}
