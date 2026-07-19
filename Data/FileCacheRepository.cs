using System.IO;
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
    DateTime LastWriteTimeUtc,
    DateTime? CreationTimeUtc,
    int? Attributes);

/// <summary>
/// ファイルシステムのスキャン結果を SQLite にキャッシュするリポジトリ。
/// 親パス単位での排他制御と、複数親パスをまとめて更新するバッチ処理を提供する。
/// </summary>
public class FileCacheRepository
{
    private const int BulkInsertBatchSize = 1000;

    // 親パス単位の排他制御用ロック。パスごとに辞書へ実体を貯めるとフォルダ数分（数十万件）
    // 無限に増え続けるため、ハッシュで固定本数のストライプへ割り当てる
    // （別パスが同じストライプに衝突しても余分な待ちが起きるだけで、正しさには影響しない）
    private const int ParentPathLockStripeCount = 128;
    private static readonly object[] ParentPathLockStripes =
        Enumerable.Range(0, ParentPathLockStripeCount).Select(_ => new object()).ToArray();

    private static int GetLockStripeIndex(string normalizedParentPath)
    {
        return (StringComparer.OrdinalIgnoreCase.GetHashCode(normalizedParentPath) & int.MaxValue) % ParentPathLockStripeCount;
    }

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
                x.LastWriteTimeUtc,
                x.CreationTimeUtc,
                x.Attributes))
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
            SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes
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
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }

        conn.Close();
        return results;
    }

    /// <summary>各ルートパス配下のファイル合計サイズをキャッシュから集計する（仮想「Folders」の一覧表示用）。</summary>
    public Dictionary<string, long> GetCachedTotalSizesUnderPaths(IEnumerable<string> rootPaths)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using var db = CreateDbContext();
        using var conn = db.Database.GetDbConnection();
        conn.Open();

        foreach (var rootPath in rootPaths)
        {
            var normalizedRootPath = PathNormalizer.Normalize(rootPath);
            if (string.IsNullOrEmpty(normalizedRootPath))
            {
                continue;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(SizeBytes), 0)
                FROM FileSystemEntries
                WHERE IsFolder = 0
                    AND FullPath LIKE @rootPattern";

            var rootPatternParam = cmd.CreateParameter();
            rootPatternParam.ParameterName = "@rootPattern";
            rootPatternParam.Value = PathNormalizer.WithTrailingSeparator(normalizedRootPath) + "%";
            cmd.Parameters.Add(rootPatternParam);

            var total = Convert.ToInt64(cmd.ExecuteScalar());
            if (total > 0)
            {
                result[normalizedRootPath] = total;
            }
        }

        conn.Close();
        return result;
    }

    /// <summary>指定パス配下から、名前に検索語を含むエントリをキャッシュから検索する。</summary>
    public List<CachedFileSystemEntry> SearchEntriesUnderPath(string rootPath, string nameQuery)
    {
        using var db = CreateDbContext();

        var normalizedRootPath = PathNormalizer.Normalize(rootPath);
        var rootWithSeparator = PathNormalizer.WithTrailingSeparator(normalizedRootPath);

        // SQLiteのLIKEはASCIIの大文字小文字を元々区別しない（lower()もASCIIのみ折り畳む）ため、
        // 行ごとに lower(Name) の文字列を生成していた従来と同じ判定を、生成コストなしで行える
        var namePattern = "%" + EscapeLikePattern(nameQuery) + "%";

        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.FullPath.StartsWith(rootWithSeparator) && EF.Functions.Like(x.Name, namePattern, "~"))
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc,
                x.CreationTimeUtc,
                x.Attributes))
            .ToList();
    }

    /// <summary>単一の親パスについて、キャッシュ済みエントリを渡されたエントリ群で置き換える。</summary>
    public void ReplaceEntriesByParentPath(string parentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        var normalizedParentPath = PathNormalizer.Normalize(parentPath);
        var lockObject = ParentPathLockStripes[GetLockStripeIndex(normalizedParentPath)];

        lock (lockObject)
        {
            ReplaceEntriesByParentPathInternal(normalizedParentPath, entries);
        }
    }

    /// <summary>
    /// 複数の親パスについて、まとめてキャッシュを置き換える（フルスキャン時に使用）。
    /// キャッシュと内容が同一の親パスは書き換えをスキップし、実際に書き換えた親パス数を返す
    /// （フルスキャンの大部分は無変化なので、これで書き込み量を大幅に減らせる）。
    /// </summary>
    public int BatchReplaceEntriesByParentPaths(IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByParentPath)
    {
        if (entriesByParentPath.Count == 0)
        {
            return 0;
        }

        var lockObjects = AcquireOrderedParentPathLocks(entriesByParentPath.Keys);

        try
        {
            using var db = CreateDbContext();

            var entriesByNormalizedParent = entriesByParentPath.ToDictionary(
                kv => PathNormalizer.Normalize(kv.Key),
                kv => kv.Value,
                StringComparer.OrdinalIgnoreCase);

            List<string> changedParentPaths;
            try
            {
                changedParentPaths = SelectChangedParentPaths(db, entriesByNormalizedParent);
            }
            catch
            {
                // 差分判定に失敗した場合は従来通り全て書き換える（書き漏らしの方が害が大きい）
                changedParentPaths = entriesByNormalizedParent.Keys.ToList();
            }

            if (changedParentPaths.Count == 0)
            {
                return 0;
            }

            using var transaction = db.Database.BeginTransaction();
            try
            {
                DeleteEntriesForParentPaths(db, changedParentPaths);
                InsertEntriesInBulk(db, BuildEntryList(changedParentPaths, entriesByNormalizedParent));
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }

            return changedParentPaths.Count;
        }
        finally
        {
            ReleaseLocks(lockObjects);
        }
    }

    /// <summary>バッチ内の各親パスについて、キャッシュ済みエントリと内容が異なるものだけを抽出する。</summary>
    private static List<string> SelectChangedParentPaths(
        ParallelScopeDbContext db,
        IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByNormalizedParent)
    {
        var parentPaths = entriesByNormalizedParent.Keys.ToList();

        var cachedByParent = db.FileSystemEntries
            .AsNoTracking()
            .Where(x => parentPaths.Contains(x.ParentPath))
            .Select(x => new CachedFileSystemEntry(
                x.ParentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc,
                x.CreationTimeUtc,
                x.Attributes))
            .ToList()
            .GroupBy(x => x.ParentPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<CachedFileSystemEntry>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var changedParentPaths = new List<string>();
        foreach (var (parentPath, liveEntries) in entriesByNormalizedParent)
        {
            cachedByParent.TryGetValue(parentPath, out var cachedEntries);
            if (!AreEntriesEquivalent(parentPath, cachedEntries ?? Array.Empty<CachedFileSystemEntry>(), liveEntries))
            {
                changedParentPaths.Add(parentPath);
            }
        }

        return changedParentPaths;
    }

    private static bool AreEntriesEquivalent(
        string normalizedParentPath,
        IReadOnlyCollection<CachedFileSystemEntry> cachedEntries,
        IReadOnlyCollection<CachedFileSystemEntry> liveEntries)
    {
        if (cachedEntries.Count != liveEntries.Count)
        {
            return false;
        }

        // FullPathはUNIQUE制約により重複しないため、集合比較で多重集合比較と等価になる
        var cachedSet = cachedEntries.Select(x => NormalizeForComparison(normalizedParentPath, x)).ToHashSet();
        return liveEntries.All(x => cachedSet.Contains(NormalizeForComparison(normalizedParentPath, x)));
    }

    /// <summary>
    /// レコードの値等価比較のための正規化。SizeBytes は書き込み経路によって NULL/0 の揺れがある
    /// （バルクINSERTは0、EF経由はNULLを書く）ため0に寄せ、ParentPath は表記揺れを正規化済みの値に寄せる。
    /// </summary>
    private static CachedFileSystemEntry NormalizeForComparison(string normalizedParentPath, CachedFileSystemEntry entry)
    {
        return entry with { ParentPath = normalizedParentPath, SizeBytes = entry.SizeBytes ?? 0L };
    }

    /// <summary>デッドロック回避のため、対象パスのストライプ番号を昇順に並べ、重複を除いてロックを取得する。</summary>
    private static List<object> AcquireOrderedParentPathLocks(IEnumerable<string> parentPaths)
    {
        var lockObjects = parentPaths
            .Select(path => GetLockStripeIndex(PathNormalizer.Normalize(path)))
            .Distinct()
            .OrderBy(index => index)
            .Select(index => ParentPathLockStripes[index])
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

    private static List<(string ParentPath, CachedFileSystemEntry Entry)> BuildEntryList(
        IReadOnlyCollection<string> normalizedParentPaths,
        IReadOnlyDictionary<string, IReadOnlyCollection<CachedFileSystemEntry>> entriesByNormalizedParent)
    {
        var allEntries = new List<(string ParentPath, CachedFileSystemEntry Entry)>();

        foreach (var parentPath in normalizedParentPaths)
        {
            foreach (var entry in entriesByNormalizedParent[parentPath])
            {
                allEntries.Add((parentPath, entry));
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
            "INSERT INTO FileSystemEntries (ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes) VALUES ");

        var parameters = new List<object>();
        for (int j = 0; j < batch.Count; j++)
        {
            var (parentPath, entry) = batch[j];
            if (j > 0)
            {
                sb.Append(",");
            }

            int pIdx = j * 8;
            sb.Append($"(@p{pIdx},@p{pIdx + 1},@p{pIdx + 2},@p{pIdx + 3},@p{pIdx + 4},@p{pIdx + 5},@p{pIdx + 6},@p{pIdx + 7})");

            parameters.Add(parentPath);
            parameters.Add(entry.FullPath);
            parameters.Add(entry.Name);
            parameters.Add(entry.IsFolder);
            parameters.Add(entry.SizeBytes ?? 0L);
            parameters.Add(entry.LastWriteTimeUtc);
            // EF書き込み経路（ReplaceEntriesByParentPathInternal）とNULL表現を揃え、差分判定の誤検知を防ぐ
            parameters.Add((object?)entry.CreationTimeUtc ?? DBNull.Value);
            parameters.Add((object?)entry.Attributes ?? DBNull.Value);
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
                LastWriteTimeUtc = x.LastWriteTimeUtc,
                CreationTimeUtc = x.CreationTimeUtc,
                Attributes = x.Attributes
            });

            db.FileSystemEntries.AddRange(entities);
        }

        db.SaveChanges();
    }

    /// <summary>parentPath 直下の各フォルダについて、配下ファイルの合計サイズをキャッシュから集計する。</summary>
    public Dictionary<string, long> GetCachedFolderTotalSizes(string parentPath, IEnumerable<string> folderPaths)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return result;
        }

        var normalizedParentPath = PathNormalizer.Normalize(parentPath);
        var parentWithSeparator = PathNormalizer.WithTrailingSeparator(normalizedParentPath);

        var folderNameToPath = BuildFolderNameLookup(folderPaths, parentWithSeparator);
        if (folderNameToPath.Count == 0)
        {
            return result;
        }

        // 配下の全ファイル行をC#へ読み出すと、親がルート級の場合は数十万行のマテリアライズが
        // フォルダ移動のたびに発生するため、直下フォルダ名（親プレフィックス直後のセグメント）
        // 単位の合計をSQL側で集計し、転送するのは直下フォルダ数分の行だけにする。
        // プレフィックス長はUTF-16とコードポイントの数え方の差を避けるためSQL側の length() で求める
        using var db = CreateDbContext();
        var conn = db.Database.GetDbConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT substr(FullPath, length(@prefix) + 1, instr(substr(FullPath, length(@prefix) + 1), @sep) - 1) AS FirstSegment,
                   SUM(COALESCE(SizeBytes, 0)) AS TotalSize
            FROM FileSystemEntries
            WHERE IsFolder = 0
              AND FullPath LIKE @prefixPattern ESCAPE '~'
              AND instr(substr(FullPath, length(@prefix) + 1), @sep) > 0 -- 親直下のファイルは子フォルダ合計に含めない
            GROUP BY FirstSegment COLLATE NOCASE";

        AddParameter(cmd, "@prefix", parentWithSeparator);
        AddParameter(cmd, "@prefixPattern", EscapeLikePattern(parentWithSeparator) + "%");
        AddParameter(cmd, "@sep", Path.DirectorySeparatorChar.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var firstSegment = reader.GetString(0);
            if (folderNameToPath.TryGetValue(firstSegment, out var folderFullPath))
            {
                result[folderFullPath] = reader.GetInt64(1);
            }
        }

        return result;
    }

    /// <summary>LIKE のワイルドカード（% _）とエスケープ文字（~）を無効化する。パス区切りの \ と衝突しないよう ~ をエスケープ文字に使う。</summary>
    private static string EscapeLikePattern(string value)
    {
        return value.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_");
    }

    private static void AddParameter(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        cmd.Parameters.Add(parameter);
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

    private ParallelScopeDbContext CreateDbContext()
    {
        return new ParallelScopeDbContext(_dbOptions);
    }
}
