using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;

namespace ParallelScope.Tests.Data;

/// <summary>
/// ファイル名索引（Plus機能）の検証。要件は「キャッシュDBへの検索と同じ結果を返すこと」なので、
/// 各ケースで <see cref="FileCacheRepository.EnumerateSearchEntriesUnderPath"/> と突き合わせる。
/// </summary>
public class FileNameIndexTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FileCacheRepository _repository;
    private readonly FileNameIndex _index;

    public FileNameIndexTests()
    {
        _repository = new FileCacheRepository(_temp.Path);
        _index = new FileNameIndex(_repository);
    }

    public void Dispose()
    {
        _repository.ReleasePooledConnections();
        _temp.Dispose();
    }

    private static CachedFileSystemEntry File(string parentPath, string name)
    {
        return new CachedFileSystemEntry(
            parentPath,
            System.IO.Path.Combine(parentPath, name),
            name,
            IsFolder: false,
            SizeBytes: 100,
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            CreationTimeUtc: null,
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

    private void SeedTree()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[]
        {
            Folder(@"C:\Root", "Reports"),
            File(@"C:\Root", "readme.txt")
        });
        _repository.ReplaceEntriesByParentPath(@"C:\Root\Reports", new[]
        {
            File(@"C:\Root\Reports", "report2026.xlsx"),
            File(@"C:\Root\Reports", "notes.txt")
        });
        // 名前の頭が同じだけの別ルート（検索範囲に混ざってはいけない）
        _repository.ReplaceEntriesByParentPath(@"C:\RootOther", new[] { File(@"C:\RootOther", "readme.txt") });
    }

    /// <summary>索引での検索が、キャッシュDBへの検索と同じ内容・同じ並びになることを確かめる。</summary>
    private void AssertMatchesDatabaseSearch(string rootPath, string query)
    {
        var expected = _repository.EnumerateSearchEntriesUnderPath(rootPath, query).ToList();
        var actual = _index.SearchUnderPath(rootPath, query);

        Assert.NotNull(actual);
        Assert.Equal(
            expected.Select(x => x.FullPath),
            actual!.Select(x => x.FullPath));
    }

    [Fact]
    public void SearchUnderPath_ReturnsNullBeforeTheIndexIsBuilt()
    {
        SeedTree();

        Assert.False(_index.IsReady);
        Assert.Null(_index.SearchUnderPath(@"C:\Root", "readme"));
    }

    [Theory]
    [InlineData("readme")]   // 起点の直下にあるファイル
    [InlineData("report")]   // 配下のフォルダとその中のファイル
    [InlineData("txt")]      // 複数階層にまたがる一致
    [InlineData("READme")]   // ASCIIの大文字小文字は無視する
    [InlineData("nothing")]  // 一致なし
    public void SearchUnderPath_MatchesTheDatabaseSearch(string query)
    {
        SeedTree();
        _index.Build();

        AssertMatchesDatabaseSearch(@"C:\Root", query);
    }

    // 索引は表示順（フォルダが先、次に名前の昇順）に並べて持ち、結果を上から順に流す。
    // キャッシュDBへの検索と並びが食い違うと、索引の有効・無効で一覧の順番が変わってしまう
    [Fact]
    public void SearchUnderPath_ReturnsTheSameOrderAsTheDatabaseSearch()
    {
        _repository.ReplaceEntriesByParentPath(@"C:\Root", new[]
        {
            File(@"C:\Root", "beta.txt"),
            File(@"C:\Root", "Alpha.txt"),
            File(@"C:\Root", "ALPHA2.txt"),
            File(@"C:\Root", "alpha1.txt"),
            Folder(@"C:\Root", "zeta"),
            Folder(@"C:\Root", "Alpha")
        });
        _index.Build();

        var expected = _repository.EnumerateSearchEntriesUnderPath(@"C:\Root", "a").Select(x => x.Name).ToList();
        var actual = _index.SearchUnderPath(@"C:\Root", "a")!.Select(x => x.Name).ToList();

        // フォルダが先に来ていること（並びが一致していれば、その中身も同じ規則で並んでいる）
        Assert.Equal(new[] { "Alpha", "zeta" }, actual.Take(2));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SearchUnderPath_ExcludesSiblingsWithTheSameNamePrefix()
    {
        SeedTree();
        _index.Build();

        var hits = _index.SearchUnderPath(@"C:\Root", "readme");

        // C:\RootOther\readme.txt は検索範囲の外
        Assert.NotNull(hits);
        Assert.Equal(new[] { @"C:\Root\readme.txt" }, hits!.Select(x => x.FullPath));
    }

    [Fact]
    public void SearchUnderPath_SearchesOnlyUnderTheGivenSubFolder()
    {
        SeedTree();
        _index.Build();

        AssertMatchesDatabaseSearch(@"C:\Root\Reports", "txt");
    }

    // 索引はキャッシュDBのある時点の写しなので、その後に変わったフォルダはDBから引き直して混ぜる
    [Fact]
    public void SearchUnderPath_ReflectsFoldersChangedAfterTheIndexWasBuilt()
    {
        SeedTree();
        _index.Build();

        _repository.ReplaceEntriesByParentPath(@"C:\Root\Reports", new[]
        {
            File(@"C:\Root\Reports", "report2026.xlsx"),
            File(@"C:\Root\Reports", "report2027.xlsx")
        });
        _index.MarkParentChanged(@"C:\Root\Reports");

        // 消えた notes.txt は出ず、増えた report2027.xlsx は出る
        AssertMatchesDatabaseSearch(@"C:\Root", "report");
        AssertMatchesDatabaseSearch(@"C:\Root", "txt");
    }

    [Fact]
    public void MarkParentChanged_DropsTheIndexWhenTooManyFoldersChanged()
    {
        SeedTree();
        _index.Build();
        Assert.True(_index.IsReady);

        // 引き直す本数が増えすぎたら索引をやめる（次のスキャンで作り直される）
        for (var i = 0; i < 300; i++)
        {
            _index.MarkParentChanged($@"C:\Root\Changed{i}");
        }

        Assert.False(_index.IsReady);
        Assert.Null(_index.SearchUnderPath(@"C:\Root", "readme"));
    }

    [Fact]
    public void Clear_MakesTheIndexUnusableSoSearchFallsBackToTheDatabase()
    {
        SeedTree();
        _index.Build();
        Assert.True(_index.IsReady);

        _index.Clear();

        Assert.False(_index.IsReady);
        Assert.Null(_index.SearchUnderPath(@"C:\Root", "readme"));
    }

    [Fact]
    public void Build_ReplacesTheSnapshotAndForgetsChangedFolders()
    {
        SeedTree();
        _index.Build();
        _index.MarkParentChanged(@"C:\Root\Reports");

        _repository.ReplaceEntriesByParentPath(@"C:\Root\Reports", new[] { File(@"C:\Root\Reports", "rebuilt.txt") });
        _index.Build();

        AssertMatchesDatabaseSearch(@"C:\Root", "txt");
    }
}
