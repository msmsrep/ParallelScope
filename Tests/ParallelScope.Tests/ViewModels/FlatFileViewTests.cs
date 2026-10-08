using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// All Filesモード（フラット表示）の取得結果の確認。取得は「貯まった分から順に出す」段階表示のため、
/// 途中経過を挟んでも最終的に全件そろうことを見る。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class FlatFileViewTests : ShellTestBase
{
    private readonly TempDirectory _root;

    public FlatFileViewTests()
    {
        _root = NewTempDirectory();
        SettingsRepository.Save(new AppSettings { RootPaths = { _root.Path } });
    }

    /// <summary>途中経過が複数回入るよう、最初の表示件数（2,000）を超える数のファイルをキャッシュへ入れる。</summary>
    private void SeedCachedFiles(int fileCount)
    {
        var subFolderPath = Path.Combine(_root.Path, "Sub");
        Directory.CreateDirectory(subFolderPath);

        FileCacheRepository.ReplaceEntriesByParentPath(subFolderPath, CacheEntries.NumberedFiles(subFolderPath, fileCount));
    }

    [Fact]
    public void FlatFileView_EventuallyShowsEveryCachedFileUnderTheFolder()
    {
        // 最初の表示（2,000件）と、その後の間隔（×8）を超えて途中経過が複数回走る件数
        const int fileCount = 17_000;
        SeedCachedFiles(fileCount);

        var viewModel = CreateViewModel();
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

        Wait.ForItemCount(tab, fileCount);
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
        FileCacheRepository.ReplaceEntriesByParentPath(otherFolderPath, new[] { CacheEntries.File(otherFolderPath, "only.txt", sizeBytes: 1) });

        var viewModel = CreateViewModel();
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));

        tab.IsFlatFileViewEnabled = true;
        // ルート配下の取得が終わる前に移動する
        Assert.True(tab.NavigateTo(otherFolderPath, addToHistory: false));

        Wait.ForItemCount(tab, 1);
        Assert.Equal("only.txt", tab.FileItems[0].Name);

        // 打ち切ったはずの取得が後から結果を流し込んでこないこと
        Thread.Sleep(500);
        Assert.Single(tab.FileItems);
        Assert.Equal("only.txt", tab.FileItems[0].Name);
    }

    // OFFに戻すと差分適用で直下一覧へ戻るが、その際に新しく加わる行（フォルダ）を末尾へ足すと
    // 「フォルダが先・名前順」の並びが崩れる（UIテスト scripts/ui-tests で見つかった）
    [Fact]
    public void FlatFileView_TurningOffRestoresTheFolderFirstOrder()
    {
        var alphaPath = Path.Combine(_root.Path, "Alpha");
        Directory.CreateDirectory(alphaPath);
        Directory.CreateDirectory(Path.Combine(_root.Path, "Beta"));
        File.WriteAllText(Path.Combine(alphaPath, "alpha1.txt"), "x");
        File.WriteAllText(Path.Combine(_root.Path, "readme.txt"), "x");
        // All Filesはキャッシュだけを引くため、ルートを開いても読み直されないAlpha配下はキャッシュへ入れておく
        FileCacheRepository.ReplaceEntriesByParentPath(alphaPath, new[] { CacheEntries.File(alphaPath, "alpha1.txt", sizeBytes: 1) });

        var viewModel = CreateViewModel();
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));
        Wait.ForItemNames(tab, "Alpha", "Beta", "readme.txt");

        tab.IsFlatFileViewEnabled = true;
        Wait.ForItemNames(tab, "alpha1.txt", "readme.txt");

        tab.IsFlatFileViewEnabled = false;
        Wait.ForItemNames(tab, "Alpha", "Beta", "readme.txt");
    }

    [Fact]
    public void FlatFileView_ShowsAllFilesWhenFewerThanTheFirstBatch()
    {
        const int fileCount = 10;
        SeedCachedFiles(fileCount);

        var viewModel = CreateViewModel();
        var tab = viewModel.ActivePane.ActiveTab;
        Assert.True(tab.NavigateTo(_root.Path, addToHistory: false));

        tab.IsFlatFileViewEnabled = true;

        Wait.ForItemCount(tab, fileCount);
    }
}
