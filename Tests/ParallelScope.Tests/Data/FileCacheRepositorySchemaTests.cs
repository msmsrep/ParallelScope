using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ParallelScope.Data;
using ParallelScope.Tests.TestSupport;

namespace ParallelScope.Tests.Data;

/// <summary>
/// 起動時のスキーマ確認（最新なら Migrate() を省く）が、新規・古い・最新のどのDBでも正しく動くことを確かめる。
/// </summary>
public class FileCacheRepositorySchemaTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string DbPath => System.IO.Path.Combine(_temp.Path, "ParallelScope.sqlite");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _temp.Dispose();
    }

    private ParallelScopeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ParallelScopeDbContext>()
            .UseSqlite($"Data Source={DbPath};")
            .Options;
        return new ParallelScopeDbContext(options);
    }

    private static CachedFileSystemEntry FileWithCreationTime(string parentPath, string name) =>
        CacheEntries.File(parentPath, name, creationTimeUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void LatestMigrationId_MatchesLastMigrationInAssembly()
    {
        using var db = CreateDbContext();

        Assert.Equal(FileCacheRepository.LatestMigrationId, db.Database.GetMigrations().Last());
    }

    [Fact]
    public void Constructor_OnNewDatabase_CreatesLatestSchemaAndIndex()
    {
        var repository = new FileCacheRepository(_temp.Path);
        repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { FileWithCreationTime(@"C:\Root", "a.txt") });

        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            repository.GetEntriesByParentPath(@"C:\Root").Single().CreationTimeUtc);
        Assert.True(IndexExists("IX_FileSystemEntries_IsFolder_FullPath"));
    }

    [Fact]
    public void Constructor_OnOutdatedDatabase_AppliesPendingMigrations()
    {
        // 作成日時・属性の列が無い時点のスキーマで止めたDB
        using (var db = CreateDbContext())
        {
            db.GetService<IMigrator>().Migrate("20260711104727_RemoveUnusedIndexes");
        }

        SqliteConnection.ClearAllPools();

        var repository = new FileCacheRepository(_temp.Path);
        repository.ReplaceEntriesByParentPath(@"C:\Root", new[] { FileWithCreationTime(@"C:\Root", "a.txt") });

        Assert.Equal(32, repository.GetEntriesByParentPath(@"C:\Root").Single().Attributes);
        using var check = CreateDbContext();
        Assert.Empty(check.Database.GetPendingMigrations());
    }

    [Fact]
    public void Constructor_OnUpToDateDatabase_KeepsExistingEntries()
    {
        var first = new FileCacheRepository(_temp.Path);
        first.ReplaceEntriesByParentPath(@"C:\Root", new[] { FileWithCreationTime(@"C:\Root", "a.txt") });
        first.ReleasePooledConnections();

        var second = new FileCacheRepository(_temp.Path);

        Assert.Equal("a.txt", second.GetEntriesByParentPath(@"C:\Root").Single().Name);
        Assert.True(IndexExists("IX_FileSystemEntries_IsFolder_FullPath"));
    }

    private bool IndexExists(string indexName)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath};");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name;";
        cmd.Parameters.AddWithValue("$name", indexName);
        return (long)cmd.ExecuteScalar()! == 1;
    }
}
