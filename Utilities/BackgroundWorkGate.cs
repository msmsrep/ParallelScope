using System.Threading;

namespace ParallelScope.Utilities;

/// <summary>
/// タブの背景処理（キャッシュ読み出し・ファイルシステム列挙・キャッシュ更新・フォルダサイズ集計）の
/// 同時実行数をアプリ全体で制限するゲート。
/// タブごとの <see cref="SingleFlightCoalescer{TRequest}"/> は1つのタブの中でしかリクエストを統合しないため、
/// タブを多く開くとタブ数ぶんの処理が一斉に走る。SQLiteの接続は1本あたり最大16MBのページキャッシュを
/// 抱え（FileCacheRepository のPRAGMA参照）、書き込みは単一ライターで直列化されるうえ、
/// スレッドプールが埋まるとUIスレッド側の待ち（DirectoryAvailabilityChecker のタイムアウト待ちなど）まで
/// 巻き添えで長引く。ここで本数を絞ることで、タブ数に関わらず負荷を一定に保つ。
/// </summary>
public sealed class BackgroundWorkGate
{
    private readonly SemaphoreSlim _semaphore;

    public BackgroundWorkGate(int maxConcurrency)
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    /// 既定の同時実行数。数本も並べればディスク待ちは隠れるため多くは要らず、
    /// 増やすほど接続ごとのページキャッシュとスレッドプールの占有だけが増える。
    /// </summary>
    public static int DefaultMaxConcurrency => Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>空きが出るまで待ってから、処理をバックグラウンドで実行する。</summary>
    public async Task<TResult> RunAsync<TResult>(Func<TResult> work)
    {
        // 枠の取得と返却はスレッドプール側で完結させる（UIスレッドが塞がっていても枠が返るようにする）
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>戻り値のない処理版。</summary>
    public async Task RunAsync(Action work)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
