using System.IO;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParallelScope.Utilities;

namespace ParallelScope.Data;

public sealed record CachedFileSystemEntry(
    string ParentPath,
    string FullPath,
    string Name,
    bool IsFolder,
    long? SizeBytes,
    DateTime LastWriteTimeUtc);

/// <summary>
/// ファイルシステムのスキャン結果を SQLite にキャッシュするリポジトリ。
/// 親パス単位での排他制御と、複数親パスをまとめて更新するバッチ処理を提供する。
/// </summary>
public class FileCacheRepository
{
    private const int BulkInsertBatchSize = 1000;

    private static readonly ConcurrentDictionary<string, object> ParentPathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly DbContextOptions<ParallelScopeDbContext> _dbOptions;

    public FileCacheRepository()
    {
        var appDataDir = AppDataPathProvider.GetOrCreateAppDataDirectory();
        var dbPath = Path.Combine(appDataDir, "ParallelScope.sqlite");

        _dbOptions = BuildDbOptions(dbPath);
        MigrateDatabaseAndApplyPragmas();
    }

    /// <summary>DbContext のオプション（SQLite接続文字列・クエリ分割設定）を構築する。</summary>
    private static DbContextOptions<ParallelScopeDbContext> BuildDbOptions(string dbPath)
    {
        // Cache=Shared は接続間でテーブルロックを共有するため、WALの「書き込み中でも読み取り可能」
        // という利点が失われ、フルスキャンの書き込み中に一覧表示の読み取りがブロックされる。
        // 既定のプライベートキャッシュ + WAL で読み書きを並行させる。
        var connectionString = $"Data Source={dbPath};";

        return new DbContextOptionsBuilder<ParallelScopeDbContext>()
            .UseSqlite(connectionString, options =>
            {
                options.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            })
            .Options;
    }

    /// <summary>マイグレーションを適用し、SQLiteのパフォーマンス関連PRAGMAを設定する。</summary>
    private void MigrateDatabaseAndApplyPragmas()
    {
        using var db = CreateDbContext();
        db.Database.Migrate();
        ApplySqlitePragmas(db);
    }

    private static void ApplySqlitePragmas(ParallelScopeDbContext db)
    {
        using var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA cache_size = -64000;
            PRAGMA temp_store = MEMORY;
            PRAGMA query_only = FALSE;
            -- チェックポイント時にWALファイルをこのサイズまで切り詰める（フルスキャンでの肥大対策）
            PRAGMA journal_size_limit = 67108864;

            -- All Files（配下再帰取得）の絞り込みを高速化
            CREATE INDEX IF NOT EXISTS IX_FileSystemEntries_IsFolder_FullPath
            ON FileSystemEntries(IsFolder, FullPath);
        ";
        cmd.ExecuteNonQuery();
        conn.Close();
    }

    /// <summary>指定した親パス直下のキャッシュ済みエントリ一覧を取得する。</summary>
    public List<CachedFileSystemEntry> GetEntriesByParentPath(string parentPath)
    {
        using var db = CreateDbContext();

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.ParentPath == parentPath)
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    /// <summary>指定パス配下の全ファイル（フォルダを除く）をキャッシュから再帰的に取得する。</summary>
    public List<CachedFileSystemEntry> GetFilesUnderPath(string rootPath)
    {
        var normalizedRootPath = PathNormalizer.Normalize(rootPath);
        var rootWithSeparator = PathNormalizer.WithTrailingSeparator(normalizedRootPath);

        using var db = CreateDbContext();
        using var conn = db.Database.GetDbConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc
            FROM FileSystemEntries
            WHERE IsFolder = 0
                            AND FullPath LIKE @rootPattern";

        var rootPatternParam = cmd.CreateParameter();
        rootPatternParam.ParameterName = "@rootPattern";
        rootPatternParam.Value = rootWithSeparator + "%";
        cmd.Parameters.Add(rootPatternParam);

        var results = new List<CachedFileSystemEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CachedFileSystemEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetDateTime(5)));
        }

        conn.Close();
        return results;
    }

    /// <summary>指定パス配下から、名前に検索語を含むエントリをキャッシュから検索する。</summary>
    public List<CachedFileSystemEntry> SearchEntriesUnderPath(string rootPath, string nameQuery)
    {
        using var db = CreateDbContext();

        var normalizedRootPath = PathNormalizer.Normalize(rootPath);
        var rootWithSeparator = PathNormalizer.WithTrailingSeparator(normalizedRootPath);

        var lowerQuery = nameQuery.ToLowerInvariant();

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.FullPath.StartsWith(rootWithSeparator) && x.Name.ToLower().Contains(lowerQuery))
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .ToList();
    }

    /// <summary>単一の親パスについて、キャッシュ済みエントリを渡されたエントリ群で置き換える。</summary>
    public void ReplaceEntriesByParentPath(string parentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        var normalizedParentPath = PathNormalizer.Normalize(parentPath);
        var lockObject = ParentPathLocks.GetOrAdd(normalizedParentPath, _ => new object());

        lock (lockObject)
        {
            ReplaceEntriesByParentPathInternal(normalizedParentPath, entries);
        }
    }

    /// <summary>複数の親パスについて、まとめてキャッシュを置き換える（フルスキャン時に使用）。</summary>
    public void BatchReplaceEntriesByParentPaths(IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByParentPath)
    {
        if (entriesByParentPath.Count == 0)
        {
            return;
        }

        var lockObjects = AcquireOrderedParentPathLocks(entriesByParentPath.Keys);

        try
        {
            using var db = CreateDbContext();
            var normalizedParentPaths = entriesByParentPath.Keys.Select(PathNormalizer.Normalize).ToList();

            using var transaction = db.Database.BeginTransaction();
            try
            {
                DeleteEntriesForParentPaths(db, normalizedParentPaths);
                InsertEntriesInBulk(db, BuildNormalizedEntryList(entriesByParentPath));
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
            ReleaseLocks(lockObjects);
        }
    }

    /// <summary>デッドロック回避のため、対象パスを正規化・ソートした順にロックを取得する。</summary>
    private static List<object> AcquireOrderedParentPathLocks(IEnumerable<string> parentPaths)
    {
        var normalizedPathsToLock = parentPaths
            .Select(PathNormalizer.Normalize)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lockObjects = normalizedPathsToLock
            .Select(path => ParentPathLocks.GetOrAdd(path, _ => new object()))
            .ToList();

        foreach (var lockObj in lockObjects)
        {
            Monitor.Enter(lockObj);
        }

        return lockObjects;
    }

    /// <summary>取得したロックを逆順に解放する。</summary>
    private static void ReleaseLocks(List<object> lockObjects)
    {
        for (int i = lockObjects.Count - 1; i >= 0; i--)
        {
            Monitor.Exit(lockObjects[i]);
        }
    }

    private static int DeleteEntriesForParentPaths(ParallelScopeDbContext db, IReadOnlyList<string> normalizedParentPaths)
    {
        var deleteParams = string.Join(",", normalizedParentPaths.Select((_, i) => $"@p{i}"));
        var deleteSql = $"DELETE FROM FileSystemEntries WHERE ParentPath IN ({deleteParams})";

        return db.Database.ExecuteSqlRaw(
            deleteSql,
            normalizedParentPaths.Select((p, i) => new Microsoft.Data.Sqlite.SqliteParameter($"@p{i}", p)).ToArray());
    }

    /// <summary>
    /// スキャン完走後に、もう不要になった行を削除する。
    /// ・スキャン済みルート配下なのに今回訪問しなかった親パスの行（ディスクから削除された/除外されたフォルダの残骸）
    /// ・configuredRootPaths が指定された場合、どのルート配下でもない行（設定からルートを外した後の残骸）
    /// </summary>
    /// <param name="scannedRootPaths">今回実際にスキャンしたルートパス。</param>
    /// <param name="configuredRootPaths">設定済みの全ルート（オフライン等でスキャンできなかったものも含む）。nullならルート外の削除は行わない（フォルダ単位スキャン用）。</param>
    /// <param name="visitedParentPaths">スキャンで実際に訪問した正規化済みフォルダパスの集合。</param>
    public int DeleteStaleEntries(
        IReadOnlyCollection<string> scannedRootPaths,
        IReadOnlyCollection<string>? configuredRootPaths,
        IReadOnlyCollection<string> visitedParentPaths)
    {
        var visitedSet = visitedParentPaths as ISet<string>
            ?? new HashSet<string>(visitedParentPaths, StringComparer.OrdinalIgnoreCase);

        var normalizedScannedRoots = scannedRootPaths
            .Select(PathNormalizer.Normalize)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        var normalizedConfiguredRoots = configuredRootPaths?
            .Select(PathNormalizer.Normalize)
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        List<string> allParentPaths;
        using (var db = CreateDbContext())
        {
            allParentPaths = db.FileSystemEntries
                .AsNoTracking()
                .Select(x => x.ParentPath)
                .Distinct()
                .ToList();
        }

        var staleParentPaths = allParentPaths
            .Where(parentPath =>
            {
                if (visitedSet.Contains(parentPath))
                {
                    return false;
                }

                if (normalizedScannedRoots.Any(root => PathNormalizer.IsAncestorOrSame(root, parentPath)))
                {
                    return true;
                }

                // スキャンできなかったルート（切断中のドライブ等）の配下は消さず、どのルート配下でもない行のみ消す
                return normalizedConfiguredRoots is not null &&
                       !normalizedConfiguredRoots.Any(root => PathNormalizer.IsAncestorOrSame(root, parentPath));
            })
            .ToList();

        if (staleParentPaths.Count == 0)
        {
            return 0;
        }

        // 対象はディスク上に存在しないフォルダなので、ライブ更新（ReplaceEntriesByParentPath）と
        // 競合する余地がなく、親パス単位のロックは取らずにバッチ削除する
        var deletedRowCount = 0;
        using (var db = CreateDbContext())
        {
            for (int i = 0; i < staleParentPaths.Count; i += BulkInsertBatchSize)
            {
                var batch = staleParentPaths.Skip(i).Take(BulkInsertBatchSize).ToList();
                deletedRowCount += DeleteEntriesForParentPaths(db, batch);
            }
        }

        return deletedRowCount;
    }

    /// <summary>
    /// WALをチェックポイントしてファイルを切り詰める。フルスキャンは全行を書き直すためWALが
    /// 数百MB規模まで肥大化することがあり、スキャン完了後に呼んで解消する（読み取り中などで
    /// 切り詰められない場合は何もしない、ベストエフォート動作）。
    /// </summary>
    public void TruncateWal()
    {
        using var db = CreateDbContext();
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
    }

    private static List<(string ParentPath, CachedFileSystemEntry Entry)> BuildNormalizedEntryList(
        IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByParentPath)
    {
        var allEntries = new List<(string ParentPath, CachedFileSystemEntry Entry)>();

        foreach (var (parentPath, entries) in entriesByParentPath)
        {
            var normalizedParentPath = PathNormalizer.Normalize(parentPath);
            foreach (var entry in entries)
            {
                allEntries.Add((normalizedParentPath, entry));
            }
        }

        return allEntries;
    }

    /// <summary>1000件ずつのバッチに分けてバルクINSERTを実行する。</summary>
    private static void InsertEntriesInBulk(ParallelScopeDbContext db, List<(string ParentPath, CachedFileSystemEntry Entry)> allEntries)
    {
        for (int i = 0; i < allEntries.Count; i += BulkInsertBatchSize)
        {
            var batch = allEntries.Skip(i).Take(BulkInsertBatchSize).ToList();
            var (sql, parameters) = BuildBulkInsertCommand(batch);
            db.Database.ExecuteSqlRaw(sql, parameters);
        }
    }

    /// <summary>複数行分の INSERT 文とパラメータを組み立てる。</summary>
    private static (string Sql, object[] Parameters) BuildBulkInsertCommand(List<(string ParentPath, CachedFileSystemEntry Entry)> batch)
    {
        var sb = new StringBuilder(
            "INSERT INTO FileSystemEntries (ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc) VALUES ");

        var parameters = new List<object>();
        for (int j = 0; j < batch.Count; j++)
        {
            var (parentPath, entry) = batch[j];
            if (j > 0)
            {
                sb.Append(",");
            }

            int pIdx = j * 6;
            sb.Append($"(@p{pIdx},@p{pIdx + 1},@p{pIdx + 2},@p{pIdx + 3},@p{pIdx + 4},@p{pIdx + 5})");

            parameters.Add(parentPath);
            parameters.Add(entry.FullPath);
            parameters.Add(entry.Name);
            parameters.Add(entry.IsFolder);
            parameters.Add(entry.SizeBytes ?? 0L);
            parameters.Add(entry.LastWriteTimeUtc);
        }

        return (sb.ToString(), parameters.ToArray());
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

    /// <summary>parentPath 直下の各フォルダについて、配下ファイルの合計サイズをキャッシュから集計する。</summary>
    public Dictionary<string, long> GetCachedFolderTotalSizes(string parentPath, IEnumerable<string> folderPaths)
    {
        using var db = CreateDbContext();

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedParentPath = PathNormalizer.Normalize(parentPath);
        var parentWithSeparator = PathNormalizer.WithTrailingSeparator(normalizedParentPath);

        var folderNameToPath = BuildFolderNameLookup(folderPaths, parentWithSeparator);
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

        return AggregateFileSizesByFolder(cachedFiles.Select(f => (f.FullPath, f.SizeBytes)), parentWithSeparator, folderNameToPath);
    }

    /// <summary>直下フォルダ名 → 正規化済みフルパス のルックアップを構築する。</summary>
    private static Dictionary<string, string> BuildFolderNameLookup(IEnumerable<string> folderPaths, string parentWithSeparator)
    {
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
                normalizedPath = PathNormalizer.Normalize(path);
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

        return folderNameToPath;
    }

    /// <summary>ファイル一覧を、直下フォルダ単位のサイズ合計に集計する（親直下のファイルは対象外）。</summary>
    private static Dictionary<string, long> AggregateFileSizesByFolder(
        IEnumerable<(string FullPath, long SizeBytes)> files,
        string parentWithSeparator,
        IReadOnlyDictionary<string, string> folderNameToPath)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var (fullPath, sizeBytes) in files)
        {
            if (fullPath.Length <= parentWithSeparator.Length)
            {
                continue;
            }

            var relative = fullPath.Substring(parentWithSeparator.Length);
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
            result[folderFullPath] = currentSize + sizeBytes;
        }

        return result;
    }

    private ParallelScopeDbContext CreateDbContext()
    {
        return new ParallelScopeDbContext(_dbOptions);
    }
}
