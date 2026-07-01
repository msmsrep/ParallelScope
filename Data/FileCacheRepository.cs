using System.IO;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParallelScope.Common;

namespace ParallelScope.Data;

public sealed record CachedFileSystemEntry(
    string ParentPath,
    string FullPath,
    string Name,
    bool IsFolder,
    long? SizeBytes,
    DateTime LastWriteTimeUtc);

public class FileCacheRepository
{
    private static readonly ConcurrentDictionary<string, object> ParentPathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly DbContextOptions<ParallelScopeDbContext> _dbOptions;

    public FileCacheRepository()
    {
        var appDataDir = AppDataLocation.GetAppDataDirectory();

        var dbPath = Path.Combine(appDataDir, "ParallelScope.sqlite");

        var connectionString = $"Data Source={dbPath};Cache=Shared;";

        _dbOptions = new DbContextOptionsBuilder<ParallelScopeDbContext>()
            .UseSqlite(connectionString, options =>
            {
                options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .Options;

        using var db = CreateDbContext();
        db.Database.Migrate();

        // PRAGMA最適化設定
        using (var conn = db.Database.GetDbConnection())
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA cache_size = -64000;
                PRAGMA temp_store = MEMORY;
                PRAGMA query_only = FALSE;
            ";
            cmd.ExecuteNonQuery();
            conn.Close();
        }
    }

    public List<CachedFileSystemEntry> GetEntriesByParentPath(string parentPath)
    {
        using var db = CreateDbContext();

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.ParentPath == parentPath)
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    public List<CachedFileSystemEntry> SearchEntriesUnderPath(string rootPath, string nameQuery)
    {
        using var db = CreateDbContext();

        var normalizedRootPath = NormalizePath(rootPath);
        var rootWithSeparator = normalizedRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedRootPath
            : normalizedRootPath + Path.DirectorySeparatorChar;

        var lowerQuery = nameQuery.ToLowerInvariant();

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.FullPath.StartsWith(rootWithSeparator) && x.Name.ToLower().Contains(lowerQuery))
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    public void ReplaceEntriesByParentPath(string parentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        var normalizedParentPath = NormalizePath(parentPath);
        var lockObject = ParentPathLocks.GetOrAdd(normalizedParentPath, _ => new object());

        lock (lockObject)
        {
            ReplaceEntriesByParentPathInternal(normalizedParentPath, entries);
        }
    }

    public void BatchReplaceEntriesByParentPaths(IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByParentPath)
    {
        if (entriesByParentPath.Count == 0)
        {
            return;
        }

        // 複数の親パスについてロックを取得
        var normalizedPathsToLock = entriesByParentPath.Keys
            .Select(NormalizePath)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lockObjects = normalizedPathsToLock
            .Select(path => ParentPathLocks.GetOrAdd(path, _ => new object()))
            .ToList();

        // 全ロック取得後に処理（デッドロック回避のため順序固定）
        foreach (var lockObj in lockObjects)
        {
            Monitor.Enter(lockObj);
        }

        try
        {
            using var db = CreateDbContext();

            var allParentPaths = entriesByParentPath.Keys.Select(NormalizePath).ToList();

            // トランザクション開始
            using var transaction = db.Database.BeginTransaction();

            try
            {
                // 対象親パスをすべて削除
                var deleteParams = string.Join(",", allParentPaths.Select((_, i) => $"@p{i}"));
                var deleteSql = $"DELETE FROM FileSystemEntries WHERE ParentPath IN ({deleteParams})";

                db.Database.ExecuteSqlRaw(
                    deleteSql,
                    allParentPaths.Select((p, i) => new Microsoft.Data.Sqlite.SqliteParameter($"@p{i}", p)).ToArray()
                );

                // すべてのエントリをバルクインサート
                var allEntries = new List<(string ParentPath, CachedFileSystemEntry Entry)>();
                foreach (var (parentPath, entries) in entriesByParentPath)
                {
                    var normalizedParentPath = NormalizePath(parentPath);
                    foreach (var entry in entries)
                    {
                        allEntries.Add((normalizedParentPath, entry));
                    }
                }

                // 1000件ずつバルクインサート
                const int bulkSize = 1000;
                for (int i = 0; i < allEntries.Count; i += bulkSize)
                {
                    var batch = allEntries.Skip(i).Take(bulkSize).ToList();
                    var sb = new StringBuilder(
                        "INSERT INTO FileSystemEntries (ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc) VALUES ");

                    var parameters = new List<object>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var (parentPath, entry) = batch[j];
                        if (j > 0) sb.Append(",");

                        int pIdx = j * 6;
                        sb.Append($"(@p{pIdx},@p{pIdx + 1},@p{pIdx + 2},@p{pIdx + 3},@p{pIdx + 4},@p{pIdx + 5})");

                        parameters.Add(parentPath);
                        parameters.Add(entry.FullPath);
                        parameters.Add(entry.Name);
                        parameters.Add(entry.IsFolder);
                        parameters.Add(entry.SizeBytes ?? 0L);
                        parameters.Add(entry.LastWriteTimeUtc);
                    }

                    db.Database.ExecuteSqlRaw(sb.ToString(), parameters.ToArray());
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        finally
        {
            // ロック解放（逆順）
            for (int i = lockObjects.Count - 1; i >= 0; i--)
            {
                Monitor.Exit(lockObjects[i]);
            }
        }
    }

    private void ReplaceEntriesByParentPathInternal(string normalizedParentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        using var db = CreateDbContext();

        // 追跡済みエンティティ削除の競合を避けるため、対象親パスを一括削除する
        db.Database.ExecuteSqlInterpolated($"DELETE FROM FileSystemEntries WHERE ParentPath = {normalizedParentPath}");

        if (entries.Count > 0)
        {
            var entities = entries.Select(x => new FileSystemEntryEntity
            {
                ParentPath = normalizedParentPath,
                FullPath = x.FullPath,
                Name = x.Name,
                IsFolder = x.IsFolder,
                SizeBytes = x.SizeBytes,
                LastWriteTimeUtc = x.LastWriteTimeUtc
            });

            db.FileSystemEntries.AddRange(entities);
        }

        db.SaveChanges();
    }

    public Dictionary<string, long> GetCachedFolderTotalSizes(string parentPath, IEnumerable<string> folderPaths)
    {
        using var db = CreateDbContext();

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedParentPath = NormalizePath(parentPath);
        var parentWithSeparator = normalizedParentPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParentPath
            : normalizedParentPath + Path.DirectorySeparatorChar;

        var folderNameToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in folderPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string normalizedPath;
            try
            {
                normalizedPath = NormalizePath(path);
            }
            catch
            {
                continue;
            }

            if (!normalizedPath.StartsWith(parentWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = normalizedPath.Substring(parentWithSeparator.Length);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var separatorIndex = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            var firstSegment = separatorIndex >= 0 ? relative[..separatorIndex] : relative;

            if (!string.IsNullOrWhiteSpace(firstSegment))
            {
                folderNameToPath[firstSegment] = normalizedPath;
            }
        }

        if (folderNameToPath.Count == 0)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var cachedFiles = db.FileSystemEntries
            .AsNoTracking()
            .Where(x => !x.IsFolder && x.FullPath.StartsWith(parentWithSeparator))
            .Select(x => new
            {
                x.FullPath,
                SizeBytes = x.SizeBytes ?? 0L
            })
            .ToList();

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in cachedFiles)
        {
            if (file.FullPath.Length <= parentWithSeparator.Length)
            {
                continue;
            }

            var relative = file.FullPath.Substring(parentWithSeparator.Length);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            var separatorIndex = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
            if (separatorIndex <= 0)
            {
                // 親フォルダ直下のファイルは子フォルダ合計に含めない
                continue;
            }

            var firstSegment = relative[..separatorIndex];
            if (!folderNameToPath.TryGetValue(firstSegment, out var folderFullPath))
            {
                continue;
            }

            result.TryGetValue(folderFullPath, out var currentSize);
            result[folderFullPath] = currentSize + file.SizeBytes;
        }

        return result;
    }

    private ParallelScopeDbContext CreateDbContext()
    {
        return new ParallelScopeDbContext(_dbOptions);
    }

    private static string NormalizePath(string path)
    {
        return PathUtility.NormalizePath(path);
    }
}
