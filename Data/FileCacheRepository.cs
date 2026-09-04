using System.Data.Common;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

    /// <param name="databaseDirectory">
    /// DBファイルを置くフォルダ。null（通常の起動時）ならアプリデータフォルダを使う。
    /// テストから一時フォルダを指定し、実際のキャッシュDBを壊さずに動かすための引数。
    /// </param>
    public FileCacheRepository(string? databaseDirectory = null)
    {
        var appDataDir = databaseDirectory ?? AppDataPathProvider.GetOrCreateAppDataDirectory();
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
            .AddInterceptors(PerConnectionPragmaInterceptor.Instance)
            .Options;
    }

    /// <summary>マイグレーションを適用し、DBファイルに永続化されるSQLite設定（WAL・インデックス）を整える。</summary>
    private void MigrateDatabaseAndApplyPragmas()
    {
        using var db = CreateDbContext();
        db.Database.Migrate();

        // journal_mode と CREATE INDEX はDBファイル側に永続化されるため起動時に1回だけ実行する。
        // 接続ごとに効くPRAGMA（synchronous等）は PerConnectionPragmaInterceptor が接続オープンの都度適用する
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;

            -- All Files（配下再帰取得）の絞り込みを高速化
            CREATE INDEX IF NOT EXISTS IX_FileSystemEntries_IsFolder_FullPath
            ON FileSystemEntries(IsFolder, FullPath);
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 接続オープン時に、接続単位でしか効かないPRAGMAを毎回適用するインターセプター。
    /// 以前は起動時の1接続にしか適用しておらず、接続プールに他の接続が増えると
    /// synchronous 等が既定値のまま動く不整合があった。PRAGMAの再適用はI/Oを伴わず数マイクロ秒で済む。
    /// </summary>
    private sealed class PerConnectionPragmaInterceptor : DbConnectionInterceptor
    {
        public static readonly PerConnectionPragmaInterceptor Instance = new();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyPerConnectionPragmas(connection);
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            ApplyPerConnectionPragmas(connection);
            return Task.CompletedTask;
        }

        private static void ApplyPerConnectionPragmas(DbConnection connection)
        {
            using var cmd = connection.CreateCommand();
            // cache_size はプールされた接続ごとに保持され続けるネイティブメモリの上限になる。
            // ホットになるのは主にインデックスページで16MBあれば十分に収まり、残りはOSのファイル
            // キャッシュが下支えするため、これ以上大きくしても体感は変わらずメモリだけ増える
            cmd.CommandText = @"
                PRAGMA synchronous = NORMAL;
                PRAGMA cache_size = -16000;
                PRAGMA temp_store = MEMORY;
                PRAGMA query_only = FALSE;
                -- チェックポイント時にWALファイルをこのサイズまで切り詰める（フルスキャンでの肥大対策）
                PRAGMA journal_size_limit = 67108864;
            ";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// プール済みのSQLite接続を破棄し、各接続が抱えるページキャッシュ等のネイティブメモリを解放する。
    /// フルスキャン後に呼ぶ想定（スキャン中の並列アクセスで接続が増え、キャッシュを持ったまま滞留するため）。
    /// 次回アクセス時の再接続コストはローカルファイルでは数ms程度で、体感には影響しない。
    /// </summary>
    public void ReleasePooledConnections()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    /// <summary>指定した親パス直下のキャッシュ済みエントリ一覧を取得する。</summary>
    public List<CachedFileSystemEntry> GetEntriesByParentPath(string parentPath)
    {
        using var db = CreateDbContext();

        // ParentPath は等値条件により全行で引数と同一値になるため、行ごとにDBから文字列を
        // 生成せず引数のインスタンスを共有する（結果は一覧表示の間保持され続ける）
        return db.FileSystemEntries
            .AsNoTracking()
            .Where(x => x.ParentPath == parentPath)
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name)
            .Select(x => new CachedFileSystemEntry(
                parentPath,
                x.FullPath,
                x.Name,
                x.IsFolder,
                x.SizeBytes,
                x.LastWriteTimeUtc,
                x.CreationTimeUtc,
                x.Attributes))
            .ToList();
    }

    /// <summary>指定パス配下の全ファイル（フォルダを除く）をキャッシュから再帰的に列挙する。</summary>
    /// <remarks>
    /// All Files表示は数十万件規模になるため、List へ全件マテリアライズせず reader から1件ずつ返し、
    /// 呼び出し側でViewModelへ直接変換させる（エントリ全件分の中間リストを持つとピークメモリが
    /// ほぼ倍増する）。列挙が終わるまで読み取り接続を保持するが、WALのため書き込みはブロックしない。
    /// </remarks>
    public IEnumerable<CachedFileSystemEntry> EnumerateFilesUnderPath(string rootPath)
    {
        using var db = CreateDbContext();
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();

        var prefixFilter = BuildPathPrefixFilter(conn, rootPath);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes
            FROM FileSystemEntries
            WHERE IsFolder = 0
              AND " + prefixFilter.WhereClause;
        prefixFilter.AddParametersTo(cmd);

        // 同じ親フォルダのファイル数だけ同一内容の ParentPath 文字列が返るため、1インスタンスへ
        // 共有する（結果はAll Files表示のViewModelから保持され続けるので、保持メモリに直結する）
        var parentPathPool = new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new CachedFileSystemEntry(
                GetPooledString(parentPathPool, reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7));
        }
    }

    /// <summary>
    /// 「FullPath が指定フォルダ配下か」を表すWHERE句と、そこへ渡すパラメータ。
    /// 使う側は WhereClause を条件へ埋め込み、AddParametersTo でパラメータを積む。
    /// </summary>
    private sealed record PathPrefixFilter(string WhereClause, IReadOnlyList<(string Name, string Value)> Parameters)
    {
        public void AddParametersTo(DbCommand cmd)
        {
            foreach (var (name, value) in Parameters)
            {
                AddParameter(cmd, name, value);
            }
        }
    }

    /// <summary>
    /// FullPath のプレフィックス絞り込み条件を組み立てる。
    /// LIKE は既定でASCIIの大文字小文字を区別しない照合になり、FullPath のインデックス（BINARY照合）を
    /// 使えないため、配下の件数に関わらずファイル行全件（百万件規模）のスキャンになる。
    /// 代わりに「区切り文字 〜 その次の文字」のレンジ検索にするとインデックスで直接引ける
    /// （区切り文字だけがこの範囲に入るため、LIKE と同じ結果になる）。
    /// ただしレンジ検索はBINARY照合＝大文字小文字を区別するので、アドレス欄への手入力等で
    /// キャッシュ内の表記と大文字小文字が食い違うと1件も引けない。そこで、その表記で1件でも
    /// 存在するかをインデックス検索（数マイクロ秒）で先に確かめ、無い場合だけ従来のLIKEに戻す。
    /// </summary>
    private static PathPrefixFilter BuildPathPrefixFilter(DbConnection conn, string rootPath)
    {
        var normalizedRootPath = PathNormalizer.Normalize(rootPath);
        var lowerBound = PathNormalizer.WithTrailingSeparator(normalizedRootPath);
        var upperBound = lowerBound[..^1] + (char)(Path.DirectorySeparatorChar + 1);

        using var probe = conn.CreateCommand();
        probe.CommandText = "SELECT 1 FROM FileSystemEntries WHERE FullPath >= @lowerBound AND FullPath < @upperBound LIMIT 1";
        AddParameter(probe, "@lowerBound", lowerBound);
        AddParameter(probe, "@upperBound", upperBound);

        if (probe.ExecuteScalar() is not null)
        {
            return new PathPrefixFilter(
                "FullPath >= @lowerBound AND FullPath < @upperBound",
                new[] { ("@lowerBound", lowerBound), ("@upperBound", upperBound) });
        }

        return new PathPrefixFilter(
            "FullPath LIKE @prefixPattern ESCAPE '~'",
            new[] { ("@prefixPattern", EscapeLikePattern(lowerBound) + "%") });
    }

    /// <summary>同一内容の文字列を1インスタンスへ共有するためのプール引き当て。</summary>
    private static string GetPooledString(Dictionary<string, string> pool, string value)
    {
        if (pool.TryGetValue(value, out var pooled))
        {
            return pooled;
        }

        pool[value] = value;
        return value;
    }

    /// <summary>各ルートパス配下のファイル合計サイズをキャッシュから集計する（仮想「Folders」の一覧表示用）。</summary>
    public Dictionary<string, long> GetCachedTotalSizesUnderPaths(IEnumerable<string> rootPaths)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using var db = CreateDbContext();
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();

        foreach (var rootPath in rootPaths)
        {
            var normalizedRootPath = PathNormalizer.Normalize(rootPath);
            if (string.IsNullOrEmpty(normalizedRootPath))
            {
                continue;
            }

            var prefixFilter = BuildPathPrefixFilter(conn, normalizedRootPath);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(SizeBytes), 0)
                FROM FileSystemEntries
                WHERE IsFolder = 0
                  AND " + prefixFilter.WhereClause;
            prefixFilter.AddParametersTo(cmd);

            var total = Convert.ToInt64(cmd.ExecuteScalar());
            if (total > 0)
            {
                result[normalizedRootPath] = total;
            }
        }

        conn.Close();
        return result;
    }

    /// <summary>指定パス配下から、名前に検索語を含むエントリをキャッシュから検索して列挙する。</summary>
    /// <remarks>
    /// インクリメンタルサーチは1文字の検索語で数十万件ヒットしうるため、List へ全件マテリアライズせず
    /// 1件ずつ返して呼び出し側でViewModelへ直接変換させる（中間リストを持つとピークメモリがほぼ倍増する）。
    /// </remarks>
    public IEnumerable<CachedFileSystemEntry> EnumerateSearchEntriesUnderPath(string rootPath, NameSearchPattern pattern)
    {
        using var db = CreateDbContext();

        // 正規表現の照合はSQLite側から呼ばせるため、接続を開く前に関数を登録する
        var conn = db.Database.GetDbConnection();
        RegisterNameMatchFunction(conn, pattern);
        db.Database.OpenConnection();

        var prefixFilter = BuildPathPrefixFilter(conn, rootPath);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes
            FROM FileSystemEntries
            WHERE " + prefixFilter.WhereClause + BuildSearchPathPreFilter(pattern) + BuildNameMatchClause(pattern) + @"
            -- 並びはファイル名索引（ASCIIの大文字へ寄せて格納）と揃える。
            -- 揃えないと、索引の有効・無効で検索結果の並びが変わってしまう
            ORDER BY IsFolder DESC, Name COLLATE NOCASE";
        prefixFilter.AddParametersTo(cmd);
        AddNameMatchParametersTo(cmd, pattern);

        // 同じ親フォルダ内のヒット件数分だけ同一内容の ParentPath 文字列が返るため、1インスタンスへ
        // 共有する（結果は検索結果表示のViewModelから保持され続けるので、保持メモリに直結する）
        var parentPathPool = new Dictionary<string, string>(StringComparer.Ordinal);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new CachedFileSystemEntry(
                GetPooledString(parentPathPool, reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7));
        }
    }

    /// <summary>ファイル名索引を組み立てるための最小限の行（行ID・親パス・名前・フォルダかどうか）。</summary>
    public readonly record struct NameIndexRow(int Id, string ParentPath, string Name, bool IsFolder);

    /// <summary>
    /// ファイル名索引の材料を全件列挙する（<see cref="FileNameIndex"/> 用）。
    /// 索引に要るのは行ID・親パス・名前だけなので、他の列は読まない。
    /// 百万件規模になるため List 化せず1件ずつ返す。
    /// </summary>
    public IEnumerable<NameIndexRow> EnumerateNameIndexRows()
    {
        using var db = CreateDbContext();
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, ParentPath, Name, IsFolder FROM FileSystemEntries";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return new NameIndexRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
        }
    }

    /// <summary>行IDを指定してエントリ本体を取り出す（索引で絞り込んだ結果の肉付けに使う）。</summary>
    /// <remarks>
    /// 並び順は指定しない。呼び出し側が索引上の位置（＝表示順）へ並べ直せるよう、行IDを添えて返す。
    /// </remarks>
    public List<(int Id, CachedFileSystemEntry Entry)> GetEntriesByIds(IReadOnlyList<int> ids)
    {
        var result = new List<(int Id, CachedFileSystemEntry Entry)>(ids.Count);
        if (ids.Count == 0)
        {
            return result;
        }

        using var db = CreateDbContext();
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();

        // 同じ親フォルダの行数だけ同一内容の ParentPath 文字列が返るため、1インスタンスへ共有する
        var parentPathPool = new Dictionary<string, string>(StringComparer.Ordinal);

        // SQLiteのパラメータ数には上限があるうえ、IN句が長すぎると解析コスト自体が効いてくるため小分けにする
        for (var start = 0; start < ids.Count; start += IdLookupChunkSize)
        {
            var chunkLength = Math.Min(IdLookupChunkSize, ids.Count - start);

            using var cmd = conn.CreateCommand();
            // 値は自前の索引が持つ行IDそのもの（外部入力ではない）だが、組み立ては数値化を通して行う
            cmd.CommandText = @"
                SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes, Id
                FROM FileSystemEntries
                WHERE Id IN (" + string.Join(",", ids.Skip(start).Take(chunkLength).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture))) + ")";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((reader.GetInt32(8), ReadEntry(reader, parentPathPool)));
            }
        }

        return result;
    }

    /// <summary>
    /// 1回のIN句にまとめる行IDの数。
    /// 小分けにしすぎると文の準備回数が効き（数千件のヒットで3倍遅い）、大きくしすぎると
    /// IN句自体の解析が重くなる（20万件で3倍遅い）。実測でこの辺りが底。
    /// </summary>
    private const int IdLookupChunkSize = 5_000;

    /// <summary>指定した親フォルダ直下から、名前に検索語を含むエントリを取り出す（索引が古い親フォルダの補完用）。</summary>
    public List<CachedFileSystemEntry> GetSearchEntriesInParent(string parentPath, NameSearchPattern pattern)
    {
        var normalizedParentPath = PathNormalizer.Normalize(parentPath);

        using var db = CreateDbContext();

        var conn = db.Database.GetDbConnection();
        RegisterNameMatchFunction(conn, pattern);
        db.Database.OpenConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes
            FROM FileSystemEntries
            WHERE ParentPath = @parentPath" + BuildNameMatchClause(pattern);
        AddParameter(cmd, "@parentPath", normalizedParentPath);
        AddNameMatchParametersTo(cmd, pattern);

        var parentPathPool = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new List<CachedFileSystemEntry>();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadEntry(reader, parentPathPool));
        }

        return result;
    }

    /// <summary>ParentPath, FullPath, Name, IsFolder, SizeBytes, LastWriteTimeUtc, CreationTimeUtc, Attributes の並びで1行読む。</summary>
    private static CachedFileSystemEntry ReadEntry(DbDataReader reader, Dictionary<string, string> parentPathPool)
    {
        return new CachedFileSystemEntry(
            GetPooledString(parentPathPool, reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetBoolean(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7));
    }

    /// <summary>この文字数以上の検索語のときだけ、FullPath による粗い絞り込みを挟む。</summary>
    private const int SearchPathPreFilterMinQueryLength = 3;

    /// <summary>
    /// 検索語による絞り込みの前段として、FullPath 側の LIKE を足す（不要なら空文字を返す）。
    /// Name は必ず FullPath の末尾なので、`Name LIKE '%語%'` が成り立つ行は必ず
    /// `FullPath LIKE '%語%'` も成り立つ（条件としては安全な、広めの絞り込みになる）。
    /// FullPath はインデックスに載っているため、ここで外れた行はテーブル本体を読まずに捨てられる。
    /// Name の判定だけだと「1件もヒットしない検索語でも配下の全行を読む」ことになり、
    /// ドライブ直下（150万行）では読み出しだけで1.6秒かかっていた（3文字以上でおよそ半分になる）。
    /// 1〜2文字ではほとんどの行が通過して二重判定になるだけなので、その場合は足さない。
    /// </summary>
    private static string BuildSearchPathPreFilter(NameSearchPattern pattern)
    {
        // 正規表現は名前の一部を含むとは限らないので、この粗い絞り込みは使えない
        return !pattern.IsRegex && pattern.Text.Length >= SearchPathPreFilterMinQueryLength
            ? @"
              AND FullPath LIKE @namePattern ESCAPE '~'"
            : string.Empty;
    }

    /// <summary>正規表現の照合をSQLから呼べるようにする（部分一致のときはLIKEで済むため何もしない）。</summary>
    /// <remarks>
    /// 絞り込みをC#側で行うと ORDER BY が配下の全行（ドライブ直下で150万行）へかかる。
    /// SQLiteに関数として渡せば、並べ替えは一致した行だけで済む。
    /// </remarks>
    private static void RegisterNameMatchFunction(DbConnection connection, NameSearchPattern pattern)
    {
        if (!pattern.IsRegex || connection is not SqliteConnection sqliteConnection)
        {
            return;
        }

        sqliteConnection.CreateFunction(NameMatchFunctionName, (string? name) => name is not null && pattern.Matches(name));
    }

    /// <summary>名前の絞り込み条件（部分一致はLIKE、正規表現は登録した関数）。</summary>
    private static string BuildNameMatchClause(NameSearchPattern pattern)
    {
        return pattern.IsRegex
            ? @"
              AND " + NameMatchFunctionName + "(Name)"
            // SQLiteのLIKEはASCIIの大文字小文字を元々区別しない（lower()もASCIIのみ折り畳む）ため、
            // 行ごとに lower(Name) の文字列を生成していた従来と同じ判定を、生成コストなしで行える
            : @"
              AND Name LIKE @namePattern ESCAPE '~'";
    }

    private static void AddNameMatchParametersTo(DbCommand command, NameSearchPattern pattern)
    {
        if (!pattern.IsRegex)
        {
            AddParameter(command, "@namePattern", "%" + EscapeLikePattern(pattern.Text) + "%");
        }
    }

    /// <summary>正規表現の照合を行う、SQLiteへ登録する関数の名前。</summary>
    private const string NameMatchFunctionName = "ps_name_matches";

    /// <summary>
    /// 単一の親パスについて、キャッシュ済みエントリを渡されたエントリ群で置き換える。
    /// キャッシュと内容が同一なら書き換えをスキップし、実際に書き換えたかどうかを返す。
    /// </summary>
    /// <remarks>
    /// フォルダを開くたびに呼ばれる一方、中身が変わっていることは稀。無変化の場合まで
    /// DELETE+INSERT（数万ファイルのフォルダでは数万行）を走らせるとフォルダ移動のたびに
    /// 書き込みとWALの肥大が発生するため、フルスキャンと同じ差分判定を通す。
    /// </remarks>
    public bool ReplaceEntriesByParentPath(string parentPath, IReadOnlyCollection<CachedFileSystemEntry> entries)
    {
        var normalizedParentPath = PathNormalizer.Normalize(parentPath);
        var lockObject = ParentPathLockStripes[GetLockStripeIndex(normalizedParentPath)];

        lock (lockObject)
        {
            var entriesByParentPath = new Dictionary<string, IReadOnlyCollection<CachedFileSystemEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [normalizedParentPath] = entries
            };

            try
            {
                using var db = CreateDbContext();
                if (SelectChangedParentPaths(db, entriesByParentPath).Count == 0)
                {
                    return false;
                }
            }
            catch
            {
                // 差分判定に失敗した場合は従来通り書き換える（書き漏らしの方が害が大きい）
            }

            ReplaceEntriesByParentPathInternal(normalizedParentPath, entries);
            return true;
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
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();
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

        // 値を裸のobjectで渡すとEFが型から変換方法を決めるため、NULL（DBNull）で
        // 「DBNull型のマッピングが無い」と落ちる。SqliteParameterに包んで渡し、NULLも通るようにする
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

            parameters.Add(CreateParameter(pIdx, parentPath));
            parameters.Add(CreateParameter(pIdx + 1, entry.FullPath));
            parameters.Add(CreateParameter(pIdx + 2, entry.Name));
            parameters.Add(CreateParameter(pIdx + 3, entry.IsFolder));
            parameters.Add(CreateParameter(pIdx + 4, entry.SizeBytes ?? 0L));
            parameters.Add(CreateParameter(pIdx + 5, entry.LastWriteTimeUtc));
            // EF書き込み経路（ReplaceEntriesByParentPathInternal）とNULL表現を揃え、差分判定の誤検知を防ぐ
            parameters.Add(CreateParameter(pIdx + 6, (object?)entry.CreationTimeUtc ?? DBNull.Value));
            parameters.Add(CreateParameter(pIdx + 7, (object?)entry.Attributes ?? DBNull.Value));
        }

        return (sb.ToString(), parameters.ToArray());
    }

    private static Microsoft.Data.Sqlite.SqliteParameter CreateParameter(int index, object value)
    {
        return new Microsoft.Data.Sqlite.SqliteParameter($"@p{index}", value);
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
        db.Database.OpenConnection();
        var conn = db.Database.GetDbConnection();

        var prefixFilter = BuildPathPrefixFilter(conn, normalizedParentPath);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT substr(FullPath, length(@prefix) + 1, instr(substr(FullPath, length(@prefix) + 1), @sep) - 1) AS FirstSegment,
                   SUM(COALESCE(SizeBytes, 0)) AS TotalSize
            FROM FileSystemEntries
            WHERE IsFolder = 0
              AND " + prefixFilter.WhereClause + @"
              AND instr(substr(FullPath, length(@prefix) + 1), @sep) > 0 -- 親直下のファイルは子フォルダ合計に含めない
            GROUP BY FirstSegment COLLATE NOCASE";

        prefixFilter.AddParametersTo(cmd);
        AddParameter(cmd, "@prefix", parentWithSeparator);
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
