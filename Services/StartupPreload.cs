using ParallelScope.Data;

namespace ParallelScope.Services;

/// <summary>
/// 起動直後に、キャッシュDB・設定ファイル・Storeのライセンス確認の準備を裏で始め、メインウィンドウの組み立てと並行させる。
/// EF Core・SQLite・System.Text.Json・WinRTのアセンブリとネイティブDLLの読み込みやStoreContextの初期化は、
/// UIスレッドで直列に払うと初回描画がそのぶん遅れるため（ファイルがOSのキャッシュに載っていない起動や、
/// パッケージ実行のStoreContextでは数百ms単位になる）。
/// </summary>
public static class StartupPreload
{
    private static Task<FileCacheRepository>? _fileCacheRepository;
    private static Task<(AppSettingsRepository Repository, AppSettings Settings)>? _settings;
    private static StoreLicenseService? _storeLicenseService;

    /// <summary>App の生成直後（App.xaml のリソース読み込みより前）に呼ぶ。</summary>
    public static void Start()
    {
        _storeLicenseService = new StoreLicenseService();
        _storeLicenseService.StartPrefetch();

        _fileCacheRepository = Task.Run(() =>
        {
            var repository = new FileCacheRepository();
            // 最初の一覧表示のクエリ準備は受け渡しを待たせずに続けて済ませる（本番のクエリとはEF側で排他される）
            _ = Task.Run(repository.WarmUpQueries);
            return repository;
        });

        // 書き込みはまだ起きないので、UIスレッドより先に読んでも内容は変わらない
        _settings = Task.Run(() =>
        {
            var repository = new AppSettingsRepository();
            return (repository, repository.Load());
        });
    }

    /// <summary>
    /// 準備済みのリポジトリを受け取る（未着手なら、従来どおりこの場で生成する）。1回だけ使える。
    /// 生成時の例外はここで投げ直され、従来と同じくウィンドウの生成が失敗する。
    /// </summary>
    public static FileCacheRepository TakeFileCacheRepository()
    {
        var task = Interlocked.Exchange(ref _fileCacheRepository, null);
        return task is null ? new FileCacheRepository() : task.GetAwaiter().GetResult();
    }

    /// <summary>準備済みの設定を受け取る（未着手なら、この場で読み込む）。1回だけ使える。</summary>
    public static (AppSettingsRepository Repository, AppSettings Settings) TakeSettings()
    {
        var task = Interlocked.Exchange(ref _settings, null);
        if (task is not null)
        {
            return task.GetAwaiter().GetResult();
        }

        var repository = new AppSettingsRepository();
        return (repository, repository.Load());
    }

    /// <summary>問い合わせを始めてあるライセンスサービスを受け取る（未着手なら新しく作る）。1回だけ使える。</summary>
    public static StoreLicenseService TakeStoreLicenseService()
    {
        return Interlocked.Exchange(ref _storeLicenseService, null) ?? new StoreLicenseService();
    }
}
