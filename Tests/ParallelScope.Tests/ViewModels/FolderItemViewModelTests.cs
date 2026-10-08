using System.Collections.Specialized;
using System.IO;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;
using ParallelScope.ViewModels;

namespace ParallelScope.Tests.ViewModels;

/// <summary>
/// フォルダツリーの1ノード（子フォルダの遅延読み込み）の確認。
/// 子が2万を超えるフォルダがあるため、読み込みの反映は「1件ずつ追加」ではなく
/// コレクションごとの差し替えで行う（件数分の CollectionChanged がUIスレッドで発生しないこと）。
/// </summary>
[Collection(SharedStateCollection.Name)]
public class FolderItemViewModelTests : IDisposable
{
    private readonly TempDirectory _root = new();

    public void Dispose() => _root.Dispose();

    private string CreateSubFolders(params string[] names)
    {
        foreach (var name in names)
        {
            Directory.CreateDirectory(Path.Combine(_root.Path, name));
        }

        return _root.Path;
    }

    [Fact]
    public async Task EnsureLoadedAsync_ReplacesTheCollectionInsteadOfAddingOneByOne()
    {
        CreateSubFolders("Alpha", "Beta", "Gamma");
        var node = new FolderItemViewModel(_root.Path);

        // 読み込み前は「読み込み中...」のダミー1件
        var placeholders = node.SubFolders;
        var placeholderChanges = 0;
        placeholders.CollectionChanged += (_, _) => placeholderChanges++;

        var subFoldersNotifications = 0;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FolderItemViewModel.SubFolders))
            {
                subFoldersNotifications++;
            }
        };

        await node.EnsureLoadedAsync();

        // 差し替えなので、元のコレクションは一度も変更されない
        Assert.Equal(0, placeholderChanges);
        Assert.NotSame(placeholders, node.SubFolders);
        Assert.Equal(1, subFoldersNotifications);
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, node.SubFolders.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task EnsureLoadedAsync_LoadsOnlyOnce()
    {
        CreateSubFolders("Alpha");
        var node = new FolderItemViewModel(_root.Path);

        await node.EnsureLoadedAsync();
        var loaded = node.SubFolders;

        await node.EnsureLoadedAsync();

        Assert.Same(loaded, node.SubFolders);
    }

    [Fact]
    public async Task EnsureLoadedAsync_HidesTheExpanderWhenThereAreNoSubFolders()
    {
        var node = new FolderItemViewModel(_root.Path);

        // 読み込み前は中身が分からないので展開ボタンを出しておく
        Assert.True(node.HasSubFolders);

        await node.EnsureLoadedAsync();

        Assert.False(node.HasSubFolders);
        Assert.Empty(node.SubFolders);
    }

    /// <summary>列挙の条件が変わったときは、読み込み済みの子を捨てて次の展開で読み直す。</summary>
    [Fact]
    public async Task Reload_DropsLoadedChildrenAndReadsThemAgain()
    {
        CreateSubFolders("Alpha");
        var node = new FolderItemViewModel(_root.Path);
        await node.EnsureLoadedAsync();

        Directory.CreateDirectory(Path.Combine(_root.Path, "Beta"));
        node.Reload();

        // 読み直す前はダミーだけに戻っている
        Assert.Single(node.SubFolders);

        await node.EnsureLoadedAsync();

        Assert.Equal(new[] { "Alpha", "Beta" }, node.SubFolders.Select(x => x.DisplayName));
    }

    private static CachedFileSystemEntry CachedFolder(string parentPath, string name)
        => new(parentPath, Path.Combine(parentPath, name), name, true, null, DateTime.UtcNow, null, null);

    /// <summary>
    /// 切断中のNASを想定し、ファイルシステムからは読めないフォルダでもキャッシュの子フォルダを出す。
    /// 読めなかったので、次の展開で読み直せるよう読み込み済みにはしない。
    /// </summary>
    [Fact]
    public async Task EnsureLoadedAsync_ShowsCachedSubFoldersWhenTheFileSystemIsUnreadable()
    {
        var offlinePath = Path.Combine(_root.Path, "Offline");
        var node = new FolderItemViewModel(
            offlinePath,
            getCachedSubFolders: path => path == offlinePath
                ? new[] { CachedFolder(offlinePath, "Beta"), CachedFolder(offlinePath, "Alpha") }
                : Array.Empty<CachedFileSystemEntry>());

        await node.EnsureLoadedAsync();
        var cached = node.SubFolders;

        Assert.Equal(new[] { "Alpha", "Beta" }, cached.Select(x => x.DisplayName));
        Assert.True(node.HasSubFolders);

        await node.BackgroundRefresh;

        // 読み直しても（まだ読めないので）キャッシュ由来の表示が残る
        await node.EnsureLoadedAsync();
        await node.BackgroundRefresh;
        Assert.Equal(new[] { "Alpha", "Beta" }, node.SubFolders.Select(x => x.DisplayName));
    }

    /// <summary>キャッシュで先に出した子は、ファイルシステムの結果で置き換えるときも使い回す（展開状態を保つため）。</summary>
    [Fact]
    public async Task EnsureLoadedAsync_MergesLiveSubFoldersIntoTheCachedOnes()
    {
        CreateSubFolders("Alpha", "Beta");
        var node = new FolderItemViewModel(
            _root.Path,
            getCachedSubFolders: path => path == _root.Path
                ? new[] { CachedFolder(_root.Path, "Alpha"), CachedFolder(_root.Path, "Stale") }
                : Array.Empty<CachedFileSystemEntry>());

        // キャッシュ由来の最初の表示を、差し替えた時点（同じスレッド）で控える
        // （ファイルシステムの結果は裏で反映されるため、await 後に読むと既に入れ替わっていることがある）
        List<string>? cachedNames = null;
        FolderItemViewModel? alpha = null;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FolderItemViewModel.SubFolders) && cachedNames is null)
            {
                cachedNames = node.SubFolders.Select(x => x.DisplayName).ToList();
                alpha = node.SubFolders[0];
            }
        };

        await node.EnsureLoadedAsync();
        await node.BackgroundRefresh;

        Assert.Equal(new[] { "Alpha", "Stale" }, cachedNames);

        Assert.Equal(new[] { "Alpha", "Beta" }, node.SubFolders.Select(x => x.DisplayName));
        Assert.Same(alpha, node.SubFolders[0]);
    }

    /// <summary>何も出せないまま読めなかった場合は、ダミーを外しつつ展開ボタンは残す（読み直す手段を残すため）。</summary>
    [Fact]
    public async Task EnsureLoadedAsync_KeepsTheExpanderWhenNothingCouldBeRead()
    {
        var node = new FolderItemViewModel(Path.Combine(_root.Path, "Missing"));

        await node.EnsureLoadedAsync();

        Assert.True(node.HasSubFolders);
        Assert.Empty(node.SubFolders);
    }
}
