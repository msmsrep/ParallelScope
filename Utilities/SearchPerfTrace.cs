using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ParallelScope.Utilities;

/// <summary>
/// 検索1回ぶんの所要時間の内訳を記録する計測用の仕組み（どこが重いかの切り分け用）。
/// 環境変数 <see cref="EnvironmentVariable"/> が "1" のときだけ有効で、アプリデータフォルダの
/// <see cref="LogFileName"/> へ1検索1行で追記する。無効なときは <see cref="Start"/> が null を返し、何もしない。
/// </summary>
/// <remarks>
/// 常時有効にしないのは、単体テスト・UIテストから実際のアプリデータフォルダへ書き込ませないため。
/// MSIX実行でもユーザー環境変数は引き継がれるので、パッケージ版の実データでも測れる。
/// </remarks>
internal sealed class SearchPerfTrace
{
    public const string EnvironmentVariable = "PARALLELSCOPE_SEARCH_PERF";
    public const string LogFileName = "search-perf.log";

    /// <summary>ログがこの大きさを超えていたら、最初の書き込みで空にしてから始める。</summary>
    private const long MaxLogBytes = 5 * 1024 * 1024;

    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";

    private static readonly object WriteGate = new();
    private static bool s_logChecked;

    /// <summary>
    /// 検索の列挙はバックグラウンドの1スレッド上で同期的に回るため、下層（ファイル名索引）から
    /// 内訳を足し込めるようスレッドに紐づけて持つ（引数で引き回すと計測のためだけに経路が汚れる）。
    /// </summary>
    [ThreadStatic]
    private static SearchPerfTrace? t_current;

    /// <summary>今のスレッドで計測中の検索（無ければ null）。</summary>
    public static SearchPerfTrace? Current => t_current;

    private readonly Stopwatch _total = Stopwatch.StartNew();
    private readonly string _query;
    private readonly bool _isRegex;
    private readonly bool _filesOnly;
    private readonly string _rootPath;

    private long _sourceTicks;
    private long _firstEntryTicks = -1;
    private long _firstPublishTicks = -1;
    private long _backgroundDoneTicks = -1;
    private long _uiQueueTicks;
    private long _uiApplyTicks;
    private int _publishCount;

    private SearchPerfTrace(string query, bool isRegex, bool filesOnly, string rootPath, TimeSpan queueWait)
    {
        _query = query;
        _isRegex = isRegex;
        _filesOnly = filesOnly;
        _rootPath = rootPath;
        QueueWait = queueWait;
    }

    public static SearchPerfTrace? Start(string query, bool isRegex, bool filesOnly, string rootPath, TimeSpan queueWait)
    {
        return IsEnabled ? new SearchPerfTrace(query, isRegex, filesOnly, rootPath, queueWait) : null;
    }

    /// <summary>入力から検索の開始までに、コアレサーの順番待ちで待たされた時間。</summary>
    public TimeSpan QueueWait { get; }

    /// <summary>結果の出どころ（narrow = 直前の結果から絞り込み、index = ファイル名索引、db = キャッシュDB）。</summary>
    public string Source { get; set; } = "?";

    public int TraversalPathCount { get; set; }

    public int HitCount { get; set; }

    /// <summary>次の入力・フォルダ移動で用済みになり、結果を出さずに終えたか。</summary>
    public bool Superseded { get; set; }

    /// <summary>ファイル名索引: 索引作成後に変わったためDBから引き直した親フォルダの数。</summary>
    public int IndexChangedParentCount { get; set; }

    /// <summary>ファイル名索引: 一致した行の本体をDBから引いた回数と時間（GetEntriesByIds）。</summary>
    public int IndexFetchCount { get; set; }

    public long IndexFetchTicks { get; set; }

    /// <summary>ファイル名索引: 引き直し分（変わった親フォルダ）をDBから読んだ時間。</summary>
    public long IndexChangedReadTicks { get; set; }

    /// <summary>計測中の検索として今のスレッドへ紐づける（Dispose で外す）。</summary>
    public Scope Enter()
    {
        var previous = t_current;
        t_current = this;
        return new Scope(previous);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly SearchPerfTrace? _previous;

        public Scope(SearchPerfTrace? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            t_current = _previous;
        }
    }

    /// <summary>結果の取り出し（DB・索引の列挙）にかかった時間だけを数えるよう包む。残りがViewModel生成等の時間になる。</summary>
    public IEnumerable<T> TimeSource<T>(IEnumerable<T> source)
    {
        using var enumerator = source.GetEnumerator();
        while (true)
        {
            var started = Stopwatch.GetTimestamp();
            var hasNext = enumerator.MoveNext();
            _sourceTicks += Stopwatch.GetTimestamp() - started;

            if (!hasNext)
            {
                yield break;
            }

            if (_firstEntryTicks < 0)
            {
                _firstEntryTicks = _total.ElapsedTicks;
            }

            yield return enumerator.Current;
        }
    }

    public void MarkBackgroundDone()
    {
        _backgroundDoneTicks = _total.ElapsedTicks;
    }

    /// <summary>画面への反映1回ぶん（UIスレッドで呼ぶ）。postedTicks は投函した時点の経過。</summary>
    public void RecordUiApply(long postedTicks, long applyTicks)
    {
        Interlocked.Increment(ref _publishCount);
        Interlocked.Add(ref _uiQueueTicks, _total.ElapsedTicks - applyTicks - postedTicks);
        Interlocked.Add(ref _uiApplyTicks, applyTicks);
        Interlocked.CompareExchange(ref _firstPublishTicks, _total.ElapsedTicks, -1);
    }

    /// <summary>計測開始からの経過（Stopwatch のティック）。</summary>
    public long ElapsedTicks => _total.ElapsedTicks;

    /// <summary>1行にまとめてログへ書く。書き込みはUIスレッドを止めないよう裏へ回す。</summary>
    public void Complete()
    {
        var line = Format();
        Debug.WriteLine(line);
        Task.Run(() => Append(line));
    }

    private string Format()
    {
        static string Ms(long ticks) => ticks < 0
            ? "-"
            : (ticks * 1000.0 / Stopwatch.Frequency).ToString("0.0", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append("\tquery=").Append(_query.Replace('\t', ' '));
        sb.Append("\tlen=").Append(_query.Length);
        if (_isRegex)
        {
            sb.Append("\tregex");
        }

        if (_filesOnly)
        {
            sb.Append("\tfilesOnly");
        }

        sb.Append("\tsource=").Append(Source);
        sb.Append("\thits=").Append(HitCount);
        if (Superseded)
        {
            sb.Append("\tsuperseded");
        }

        sb.Append("\ttotalMs=").Append(Ms(_total.ElapsedTicks));
        sb.Append("\tqueueWaitMs=").Append(Ms(QueueWait.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond));
        sb.Append("\tfirstEntryMs=").Append(Ms(_firstEntryTicks));
        sb.Append("\tfirstShownMs=").Append(Ms(Interlocked.Read(ref _firstPublishTicks)));
        sb.Append("\tbackgroundMs=").Append(Ms(_backgroundDoneTicks));
        sb.Append("\tsourceMs=").Append(Ms(_sourceTicks));
        sb.Append("\tvmEtcMs=").Append(Ms(_backgroundDoneTicks < 0 ? -1 : Math.Max(0, _backgroundDoneTicks - _sourceTicks)));
        sb.Append("\tuiQueueMs=").Append(Ms(Interlocked.Read(ref _uiQueueTicks)));
        sb.Append("\tuiApplyMs=").Append(Ms(Interlocked.Read(ref _uiApplyTicks)));
        sb.Append("\tpublishes=").Append(Volatile.Read(ref _publishCount));

        if (Source == "index")
        {
            sb.Append("\tidxFetchMs=").Append(Ms(IndexFetchTicks));
            sb.Append("\tidxFetches=").Append(IndexFetchCount);
            sb.Append("\tidxChangedParents=").Append(IndexChangedParentCount);
            sb.Append("\tidxChangedReadMs=").Append(Ms(IndexChangedReadTicks));
        }

        sb.Append("\troots=").Append(TraversalPathCount);
        sb.Append("\tpath=").Append(_rootPath);
        return sb.ToString();
    }

    private static void Append(string line)
    {
        try
        {
            lock (WriteGate)
            {
                var path = Path.Combine(AppDataPathProvider.GetOrCreateAppDataDirectory(), LogFileName);
                if (!s_logChecked)
                {
                    s_logChecked = true;
                    if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                    {
                        File.Delete(path);
                    }
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // 計測用のログなので、書けなくても本体の動作には影響させない
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
