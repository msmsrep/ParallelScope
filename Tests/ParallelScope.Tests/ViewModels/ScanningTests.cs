using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.Utilities;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// フルスキャン・フォルダ単位スキャンがファイルシステムをキャッシュへ写し取り、
/// 完走した場合だけ残骸を掃除することの確認。走査対象は実在の一時フォルダ。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class ScanningTests : ShellTestBase
{
    private readonly string _root;

    public ScanningTests()
    {
        _root = PathNormalizer.Normalize(NewTempDirectory().Path);
        Directory.CreateDirectory(Path.Combine(_root, "Sub", "Deep"));
        File.WriteAllText(Path.Combine(_root, "top.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "Sub", "middle.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "Sub", "Deep", "bottom.txt"), "x");
        PinFolderTimestamps();
    }

    /// <summary>
    /// フォルダの更新日時を固定する。親フォルダの一覧に載るフォルダの更新日時はNTFSが遅れて反映するため、
    /// 作った直後のフォルダでは1回目と2回目のスキャンで値が食い違い、「変化なし」の判定が揺れる。
    /// </summary>
    private void PinFolderTimestamps()
    {
        var pinned = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var folder in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories).Reverse())
        {
            Directory.SetLastWriteTimeUtc(folder, pinned);
        }
    }

    private void SaveRoots(IEnumerable<string> rootPaths, IEnumerable<string>? excludedPaths = null)
    {
        // 起動時にタブが最初のルートを開いて裏で読み直し、作成日時付きの行をキャッシュへ書く。
        // スキャンは作成日時を書かないため、走査対象を最初のルートにすると両者の書き込みが競合して
        // 差分の件数が揺れる。タブには空のフォルダを開かせておく
        var settings = new AppSettings();
        settings.RootPaths.Add(NewTempDirectory().Path);
        settings.RootPaths.AddRange(rootPaths);
        settings.ExcludedPaths.AddRange(excludedPaths ?? Array.Empty<string>());
        SettingsRepository.Save(settings);
    }

    private IReadOnlyList<string> CachedNames(params string[] relativeParentPath)
    {
        var parentPath = Path.Combine(new[] { _root }.Concat(relativeParentPath).ToArray());
        return FileCacheRepository.GetEntriesByParentPath(parentPath).Select(x => x.Name).ToList();
    }

    [Fact]
    public async Task FullScan_WritesEveryFolderUnderTheRoot()
    {
        SaveRoots(new[] { _root });
        var viewModel = CreateViewModel();

        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(new[] { "Sub", "top.txt" }, CachedNames());
        Assert.Equal(new[] { "Deep", "middle.txt" }, CachedNames("Sub"));
        Assert.Equal(new[] { "bottom.txt" }, CachedNames("Sub", "Deep"));
    }

    // 書き込みは差分方式で、戻り値は実際に書き換えた親フォルダ数（変化が無ければ書かない）
    [Fact]
    public async Task FullScan_RewritesNothingWhenTheFileSystemIsUnchanged()
    {
        SaveRoots(new[] { _root });
        var viewModel = CreateViewModel();
        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(0, await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None));

        File.WriteAllText(Path.Combine(_root, "Sub", "added.txt"), "x");
        PinFolderTimestamps();
        Assert.Equal(1, await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FullScan_RemovesTheCacheOfDeletedFolders()
    {
        SaveRoots(new[] { _root });
        var viewModel = CreateViewModel();
        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        Directory.Delete(Path.Combine(_root, "Sub"), recursive: true);
        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        // 消えたフォルダ自身の行は親の書き換えで、配下の行は完走後の残骸掃除で消える
        Assert.Equal(new[] { "top.txt" }, CachedNames());
        Assert.Empty(CachedNames("Sub"));
        Assert.Empty(CachedNames("Sub", "Deep"));
    }

    [Fact]
    public async Task FullScan_SkipsExcludedFolders()
    {
        SaveRoots(new[] { _root }, excludedPaths: new[] { Path.Combine(_root, "Sub") });
        var viewModel = CreateViewModel();

        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        Assert.Empty(CachedNames("Sub"));
        Assert.Empty(CachedNames("Sub", "Deep"));
    }

    // 切断中のドライブ等のキャッシュを誤削除しないよう、見えないルートは走査も掃除もしない
    [Fact]
    public async Task FullScan_KeepsTheCacheOfRootsThatCannotBeFound()
    {
        // 走査するルートの外に置く（配下に置くと、そのルートの残骸として正しく消される）
        var missingRoot = PathNormalizer.Normalize(Path.Combine(NewTempDirectory().Path, "Missing"));
        FileCacheRepository.ReplaceEntriesByParentPath(missingRoot, new[] { CacheEntries.File(missingRoot, "kept.txt") });
        SaveRoots(new[] { _root, missingRoot });
        var viewModel = CreateViewModel();

        await viewModel.FullScanConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(new[] { "kept.txt" }, FileCacheRepository.GetEntriesByParentPath(missingRoot).Select(x => x.Name));
    }

    // 途中で止めたスキャンでは「未訪問＝削除された」と区別できないため、残骸の掃除をしない
    [Fact]
    public async Task CancelledFullScan_ThrowsAndKeepsUnvisitedCache()
    {
        var staleParent = Path.Combine(_root, "Gone");
        FileCacheRepository.ReplaceEntriesByParentPath(staleParent, new[] { CacheEntries.File(staleParent, "stale.txt") });
        SaveRoots(new[] { _root });
        var viewModel = CreateViewModel();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => viewModel.FullScanConfiguredRootsAsync(cancellation.Token));

        Assert.Equal(new[] { "stale.txt" }, CachedNames("Gone"));
    }

    [Fact]
    public async Task FolderScan_UpdatesOnlyUnderTheGivenFolder()
    {
        SaveRoots(new[] { _root });
        var viewModel = CreateViewModel();
        var siblingParent = Path.Combine(_root, "Sibling");
        FileCacheRepository.ReplaceEntriesByParentPath(siblingParent, new[] { CacheEntries.File(siblingParent, "untouched.txt") });
        var staleUnderSub = Path.Combine(_root, "Sub", "Gone");
        FileCacheRepository.ReplaceEntriesByParentPath(staleUnderSub, new[] { CacheEntries.File(staleUnderSub, "stale.txt") });

        await viewModel.ScanFolderSubtreeAsync(Path.Combine(_root, "Sub"));

        Assert.Equal(new[] { "Deep", "middle.txt" }, CachedNames("Sub"));
        Assert.Equal(new[] { "bottom.txt" }, CachedNames("Sub", "Deep"));
        // 掃除はスキャンしたフォルダの配下に限る（兄弟フォルダはスキャンしていないので残す）
        Assert.Empty(CachedNames("Sub", "Gone"));
        Assert.Equal(new[] { "untouched.txt" }, CachedNames("Sibling"));
    }

    [Fact]
    public async Task FolderScan_DoesNothingForExcludedFolders()
    {
        var sub = Path.Combine(_root, "Sub");
        SaveRoots(new[] { _root }, excludedPaths: new[] { sub });
        var viewModel = CreateViewModel();

        Assert.Equal(0, await viewModel.ScanFolderSubtreeAsync(sub));
        Assert.Empty(CachedNames("Sub"));
    }
}
