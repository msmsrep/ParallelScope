using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;

namespace ParallelScope.Tests.Data;

/// <summary>
/// SQLiteキャッシュの読み書きを、一時フォルダに作った実DBに対して検証する。
/// （ファイルシステムには触れないので、パスは実在しなくてよい）
/// </summary>
public class FileCacheRepositoryTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _repository;

    public FileCacheRepositoryTests()
    {
        _repository = new FileCacheRepository(_temp.Path);
    }

    public void Dispose()
    {
        // 接続プールがDBファイルを掴んだままだと一時フォルダを消せない
        _repository.ReleasePooledConnections();
        _temp.Dispose();
    }

    private static CachedFileSystemEntry File(string parentPath, string name, long sizeBytes = 100)
    {
        return new CachedFileSystemEntry(
            parentPath,
            System.IO.Path.Combine(parentPath, name),
            name,
            IsFolder: false,
            sizeBytes,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Attributes: 32);
    }

    private static CachedFileSystemEntry Folder(string parentPath, string name)
    {
        return new CachedFileSystemEntry(
            parentPath,
            System.IO.Path.Combine(parentPath, name),
            name,
            IsFolder: true,
            SizeBytes: null,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreationTimeUtc: null,
            Attributes: 16);
    }

    [Fact]
    public void GetEntriesByParentPath_ReturnsFoldersFirstThenNamesAscending()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[]
        {
            File(@"C:\Root", "b.txt"),
            Folder(@"C:\Root", "Zebra"),
            File(@"C:\Root", "a.txt"),
            Folder(@"C:\Root", "Alpha")
        });

        var entries = _repository.GetEntriesByParentPath(@"C:\Root");

        Assert.Equal(new[] { "Alpha", "Zebra", "a.txt", "b.txt" }, entries.Select(e => e.Name));
        Assert.Equal(new[] { true, true, false, false }, entries.Select(e => e.IsFolder));
    }

    [Fact]
    public void GetEntriesByParentPath_RoundTripsAllColumns()
    {
        var entry = File(@"C:\Root", "a.txt", 4096);
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { entry });

        var loaded = Assert.Single(_repository.GetEntriesByParentPath(@"C:\Root"));

        Assert.Equal(entry.FullPath, loaded.FullPath);
        Assert.Equal(entry.Name, loaded.Name);
        Assert.Equal(entry.SizeBytes, loaded.SizeBytes);
        Assert.Equal(entry.LastWriteTimeUtc, loaded.LastWriteTimeUtc);
        Assert.Equal(entry.CreationTimeUtc, loaded.CreationTimeUtc);
        Assert.Equal(entry.Attributes, loaded.Attributes);
    }

    [Fact]
    public void GetEntriesByParentPath_ReturnsEmptyForUnknownParent()
    {
        Assert.Empty(_repository.GetEntriesByParentPath(@"C:\Nothing"));
    }

    [Fact]
    public void ReplaceEntriesByParentPath_RemovesEntriesThatNoLongerExist()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { File(@"C:\Root", "old.txt") });

        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { File(@"C:\Root", "new.txt") });

        var entries = _repository.GetEntriesByParentPath(@"C:\Root");
        Assert.Equal(new[] { "new.txt" }, entries.Select(e => e.Name));
    }

    [Fact]
    public void ReplaceEntriesByParentPath_NormalizesTheParentPath()
    {
        // 末尾の区切りや大文字小文字が違っても同じ親として扱われる
        _repository.ReplaceEntriesByParentPath(@"C:\Root\", new[] { File(@"C:\Root", "a.txt") });

        Assert.Single(_repository.GetEntriesByParentPath(@"C:\Root"));
    }

    [Fact]
    public void BatchReplaceEntriesByParentPaths_ReturnsNumberOfParentsActuallyRewritten()
    {
        var batch = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>
        {
            [@"C:\Root"] = new[] { Folder(@"C:\Root", "Sub") },
            [@"C:\Root\Sub"] = new[] { File(@"C:\Root\Sub", "a.txt") }
        };

        Assert.Equal(2, _repository.BatchReplaceEntriesByParentPaths(batch));

        // 内容が同じ2回目は書き換えをスキップする（フルスキャンの大部分は無変化なので書き込み量を抑える）
        Assert.Equal(0, _repository.BatchReplaceEntriesByParentPaths(batch));

        batch[@"C:\Root\Sub"] = new[] { File(@"C:\Root\Sub", "a.txt", sizeBytes: 999) };
        Assert.Equal(1, _repository.BatchReplaceEntriesByParentPaths(batch));
    }

    [Fact]
    public void BatchReplaceEntriesByParentPaths_ReturnsZeroForEmptyBatch()
    {
        Assert.Equal(0, _repository.BatchReplaceEntriesByParentPaths(
            new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>()));
    }

    [Fact]
    public void BatchReplaceEntriesByParentPaths_DetectsRemovedEntryEvenWhenCountMatches()
    {
        var batch = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>
        {
            [@"C:\Root"] = new[] { File(@"C:\Root", "a.txt"), File(@"C:\Root", "b.txt") }
        };
        _repository.BatchReplaceEntriesByParentPaths(batch);

        // 件数が同じでも中身が入れ替わっていれば書き換え対象になる
        batch[@"C:\Root"] = new[] { File(@"C:\Root", "a.txt"), File(@"C:\Root", "c.txt") };

        Assert.Equal(1, _repository.BatchReplaceEntriesByParentPaths(batch));
        Assert.Equal(new[] { "a.txt", "c.txt" }, _repository.GetEntriesByParentPath(@"C:\Root").Select(e => e.Name));
    }

    [Fact]
    public void BatchReplaceEntriesByParentPaths_StoresEntriesWithoutCreationTimeOrAttributes()
    {
        // 作成日時・属性の取得に失敗したエントリ（両方null）でもバルクINSERTが通ること
        var entry = new CachedFileSystemEntry(
            @"C:\Root", @"C:\Root\a.txt", "a.txt", IsFolder: false, SizeBytes: null,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), CreationTimeUtc: null, Attributes: null);

        _repository.BatchReplaceEntriesByParentPaths(new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>
        {
            [@"C:\Root"] = new[] { entry }
        });

        var loaded = Assert.Single(_repository.GetEntriesByParentPath(@"C:\Root"));
        Assert.Null(loaded.CreationTimeUtc);
        Assert.Null(loaded.Attributes);
        // SizeBytesのnullは0として書き込まれる（EF経路とのNULL/0の揺れを差分判定側で吸収するため）
        Assert.Equal(0, loaded.SizeBytes);
    }

    [Fact]
    public void EnumerateFilesUnderPath_ReturnsFilesRecursivelyAndSkipsFolders()
    {
        SeedTree();

        var files = _repository.EnumerateFilesUnderPath(@"C:\Root").ToList();

        Assert.Equal(
            new[] { @"C:\Root\Sub\Deep\deep.txt", @"C:\Root\Sub\b.txt", @"C:\Root\a.txt" }.OrderBy(x => x),
            files.Select(f => f.FullPath).OrderBy(x => x));
        Assert.All(files, f => Assert.False(f.IsFolder));
    }

    [Fact]
    public void EnumerateFilesUnderPath_ExcludesSiblingsWithTheSameNamePrefix()
    {
        SeedTree();
        _repository.ReplaceEntriesByParentPath(@"C:\RootOther", new[] { File(@"C:\RootOther", "other.txt") });

        var files = _repository.EnumerateFilesUnderPath(@"C:\Root").ToList();

        Assert.DoesNotContain(files, f => f.Name == "other.txt");
    }

    [Fact]
    public void EnumerateSearchEntriesUnderPath_MatchesNameSubstringCaseInsensitively()
    {
        SeedTree();

        var hits = _repository.EnumerateSearchEntriesUnderPath(@"C:\Root", "DEEP").ToList();

        Assert.Equal(new[] { "Deep", "deep.txt" }, hits.Select(h => h.Name));
        // フォルダが先、その後に名前昇順
        Assert.True(hits[0].IsFolder);
    }

    [Fact]
    public void EnumerateSearchEntriesUnderPath_TreatsWildcardCharactersLiterally()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[]
        {
            File(@"C:\Root", "a_b.txt"),
            File(@"C:\Root", "axb.txt"),
            File(@"C:\Root", "100%.txt")
        });

        // LIKEのワイルドカード（_ %）が素通りすると "a_b" が "axb" にもヒットしてしまう
        Assert.Equal(new[] { "a_b.txt" },
            _repository.EnumerateSearchEntriesUnderPath(@"C:\Root", "a_b").Select(e => e.Name));
        Assert.Equal(new[] { "100%.txt" },
            _repository.EnumerateSearchEntriesUnderPath(@"C:\Root", "100%").Select(e => e.Name));
    }

    // 3文字以上の検索語では、テーブル本体を読む前にFullPathで粗く絞る（Nameの判定はその後）。
    // 粗い絞り込みが素通しするフォルダ名だけの一致を、Nameの判定がきちんと落とすことを見る
    [Theory]
    [InlineData("Sub")]   // 3文字＝粗い絞り込みが入る
    [InlineData("Su")]    // 2文字＝入らない
    public void EnumerateSearchEntriesUnderPath_DoesNotMatchOnTheFolderPartOfThePath(string query)
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root\Subway", new[]
        {
            File(@"C:\Root\Subway", "ticket.txt"),
            File(@"C:\Root\Subway", "Subtotal.txt")
        });

        var hits = _repository.EnumerateSearchEntriesUnderPath(@"C:\Root", query).ToList();

        // 親フォルダ名（Subway）が一致するだけの ticket.txt は含まれない
        Assert.Equal(new[] { "Subtotal.txt" }, hits.Select(h => h.Name));
    }

    [Fact]
    public void EnumerateSearchEntriesUnderPath_SearchesOnlyUnderTheGivenRoot()
    {
        SeedTree();

        var hits = _repository.EnumerateSearchEntriesUnderPath(@"C:\Root\Sub", ".txt").ToList();

        // 検索範囲の外にある C:\Root\a.txt は含まれない
        Assert.Equal(new[] { "b.txt", "deep.txt" }, hits.Select(h => h.Name));
    }

    [Fact]
    public void GetCachedFolderTotalSizes_SumsFilesUnderEachChildFolder()
    {
        SeedTree();

        var sizes = _repository.GetCachedFolderTotalSizes(@"C:\Root", new[] { @"C:\Root\Sub" });

        // Sub 直下の b.txt(100) + Deep/deep.txt(100)。親直下の a.txt は含まない
        Assert.Equal(200, sizes[@"C:\Root\Sub"]);
        Assert.Single(sizes);
    }

    [Fact]
    public void GetCachedFolderTotalSizes_IgnoresFoldersOutsideTheParent()
    {
        SeedTree();

        var sizes = _repository.GetCachedFolderTotalSizes(@"C:\Root", new[] { @"D:\Elsewhere" });

        Assert.Empty(sizes);
    }

    [Fact]
    public void GetCachedFolderTotalSizes_ReturnsEmptyForBlankParent()
    {
        Assert.Empty(_repository.GetCachedFolderTotalSizes("  ", new[] { @"C:\Root\Sub" }));
    }

    [Fact]
    public void GetCachedTotalSizesUnderPaths_SumsEachRootAndSkipsEmptyOnes()
    {
        SeedTree();

        var sizes = _repository.GetCachedTotalSizesUnderPaths(new[] { @"C:\Root\", @"D:\Empty" });

        Assert.Equal(300, sizes[@"C:\Root"]);
        // 合計0のルートは結果に載せない（サイズ未取得として空表示にするため）
        Assert.False(sizes.ContainsKey(@"D:\Empty"));
    }

    [Fact]
    public void DeleteStaleEntries_RemovesUnvisitedParentsUnderScannedRoots()
    {
        SeedTree();

        var deleted = _repository.DeleteStaleEntries(
            scannedRootPaths: new[] { @"C:\Root" },
            configuredRootPaths: null,
            visitedParentPaths: new[] { @"C:\Root", @"C:\Root\Sub" });

        // 訪問しなかった C:\Root\Sub\Deep の行だけが消える
        Assert.Equal(1, deleted);
        Assert.Empty(_repository.GetEntriesByParentPath(@"C:\Root\Sub\Deep"));
        Assert.NotEmpty(_repository.GetEntriesByParentPath(@"C:\Root\Sub"));
    }

    [Fact]
    public void DeleteStaleEntries_KeepsEntriesUnderConfiguredButUnscannedRoots()
    {
        SeedTree();
        _repository.ReplaceEntriesByParentPath(@"D:\Offline", new[] { File(@"D:\Offline", "x.txt") });

        var deleted = _repository.DeleteStaleEntries(
            scannedRootPaths: new[] { @"C:\Root" },
            configuredRootPaths: new[] { @"C:\Root", @"D:\Offline" },
            visitedParentPaths: new[] { @"C:\Root", @"C:\Root\Sub", @"C:\Root\Sub\Deep" });

        // 切断中のドライブ等、スキャンできなかったルート配下のキャッシュは残す
        Assert.Equal(0, deleted);
        Assert.NotEmpty(_repository.GetEntriesByParentPath(@"D:\Offline"));
    }

    [Fact]
    public void DeleteStaleEntries_RemovesEntriesOutsideEveryConfiguredRoot()
    {
        SeedTree();
        _repository.ReplaceEntriesByParentPath(@"D:\Removed", new[] { File(@"D:\Removed", "x.txt") });

        var deleted = _repository.DeleteStaleEntries(
            scannedRootPaths: new[] { @"C:\Root" },
            configuredRootPaths: new[] { @"C:\Root" },
            visitedParentPaths: new[] { @"C:\Root", @"C:\Root\Sub", @"C:\Root\Sub\Deep" });

        // 設定からルートを外した後の残骸を掃除する
        Assert.Equal(1, deleted);
        Assert.Empty(_repository.GetEntriesByParentPath(@"D:\Removed"));
    }

    [Fact]
    public void DeleteStaleEntries_ReturnsZeroWhenNothingIsStale()
    {
        SeedTree();

        var deleted = _repository.DeleteStaleEntries(
            scannedRootPaths: new[] { @"C:\Root" },
            configuredRootPaths: null,
            visitedParentPaths: new[] { @"C:\Root", @"C:\Root\Sub", @"C:\Root\Sub\Deep" });

        Assert.Equal(0, deleted);
    }

    [Fact]
    public void TruncateWal_DoesNotThrow()
    {
        SeedTree();

        _repository.TruncateWal();

        Assert.NotEmpty(_repository.GetEntriesByParentPath(@"C:\Root"));
    }

    /// <summary>C:\Root - Sub - Deep の3階層に、各階層1ファイル（100バイト）を置く。</summary>
    // 配下の絞り込みはインデックス（BINARY照合＝大文字小文字を区別）のレンジ検索で行い、
    // その表記で1件も無いときだけ大文字小文字を区別しないLIKEに戻す。
    // アドレス欄への手入力などで表記が食い違っても結果が変わらないことを確かめる
    [Fact]
    public void EnumerateFilesUnderPath_FindsFilesWhenRootCasingDiffersFromCache()
    {
        SeedTree();

        var files = _repository.EnumerateFilesUnderPath(@"C:\root\sub").ToList();

        Assert.Equal(
            new[] { @"C:\Root\Sub\Deep\deep.txt", @"C:\Root\Sub\b.txt" }.OrderBy(x => x),
            files.Select(f => f.FullPath).OrderBy(x => x));
    }

    [Fact]
    public void EnumerateSearchEntriesUnderPath_FindsHitsWhenRootCasingDiffersFromCache()
    {
        SeedTree();

        var hits = _repository.EnumerateSearchEntriesUnderPath(@"C:\root\sub", ".txt").ToList();

        Assert.Equal(new[] { "b.txt", "deep.txt" }, hits.Select(h => h.Name));
    }

    [Fact]
    public void GetCachedFolderTotalSizes_SumsWhenParentCasingDiffersFromCache()
    {
        SeedTree();

        var sizes = _repository.GetCachedFolderTotalSizes(@"C:\root", new[] { @"C:\Root\Sub" });

        Assert.Equal(200, sizes[@"C:\Root\Sub"]);
    }

    [Fact]
    public void GetCachedTotalSizesUnderPaths_SumsWhenRootCasingDiffersFromCache()
    {
        SeedTree();

        var sizes = _repository.GetCachedTotalSizesUnderPaths(new[] { @"C:\root" });

        Assert.Equal(300, sizes[@"C:\root"]);
    }

    // フォルダを開くたびに呼ばれるため、中身が変わっていない間はDELETE+INSERTを走らせない
    [Fact]
    public void ReplaceEntriesByParentPath_SkipsWriteWhenContentIsUnchanged()
    {
        var entries = new[] { File(@"C:\Root", "a.txt"), Folder(@"C:\Root", "Sub") };

        Assert.True(_repository.ReplaceEntriesByParentPath(@"C:\Root", entries));
        Assert.False(_repository.ReplaceEntriesByParentPath(@"C:\Root", entries));

        Assert.Equal(
            new[] { "Sub", "a.txt" },
            _repository.GetEntriesByParentPath(@"C:\Root").Select(x => x.Name));
    }

    [Fact]
    public void ReplaceEntriesByParentPath_WritesWhenContentChanged()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { File(@"C:\Root", "a.txt") });

        Assert.True(_repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { File(@"C:\Root", "a.txt", sizeBytes: 999) }));

        var loaded = Assert.Single(_repository.GetEntriesByParentPath(@"C:\Root"));
        Assert.Equal(999, loaded.SizeBytes);
    }

    private void SeedTree()
    {
        _repository.BatchReplaceEntriesByParentPaths(new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>
        {
            [@"C:\Root"] = new[] { Folder(@"C:\Root", "Sub"), File(@"C:\Root", "a.txt") },
            [@"C:\Root\Sub"] = new[] { Folder(@"C:\Root\Sub", "Deep"), File(@"C:\Root\Sub", "b.txt") },
            [@"C:\Root\Sub\Deep"] = new[] { File(@"C:\Root\Sub\Deep", "deep.txt") }
        });
    }
}
