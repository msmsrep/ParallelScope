using System.Threading;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// 大量アイテムの入れ替え（All Filesモード・検索）の後に、不要になった一覧のメモリをOSへ返す処理。
/// 数十万件規模の入れ替えではGen2/LOHに旧一覧が残り、自然なGCまで（さらにGC後もコミット済みのまま）
/// ワーキングセットが積み上がるため、明示的に回収・返却する。
/// アプリ全体で1本にまとめてあるのは、タブごとに予約するとタブ数ぶんのブロッキングGCが
/// 立て続けに走り、回収そのものが固まりの原因になるため。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>この件数を超える一覧の入れ替えの後は、メモリ返却のためのGCを予約する。</summary>
    private const int MemoryTrimItemCountThreshold = 50_000;

    /// <summary>連続したナビゲーション中に毎回GCが走らないよう、この時間だけ静止するのを待つ。</summary>
    private static readonly TimeSpan MemoryTrimQuietPeriod = TimeSpan.FromSeconds(2);

    private int _memoryTrimVersion;

    /// <summary>
    /// 大量アイテムの入れ替えが起きたことを受けて、静止後に1回だけメモリ回収を行う。
    /// どのタブから何度予約されても、最後の予約から静止時間が経った1回だけが実行される。
    /// </summary>
    private void RequestMemoryTrim(int replacedItemCount)
    {
        if (replacedItemCount < MemoryTrimItemCountThreshold)
        {
            return;
        }

        var version = Interlocked.Increment(ref _memoryTrimVersion);
        _ = Task.Run(async () =>
        {
            await Task.Delay(MemoryTrimQuietPeriod).ConfigureAwait(false);
            if (version != Volatile.Read(ref _memoryTrimVersion))
            {
                return;
            }

            // プールされたSQLite接続が抱えるページキャッシュ（接続あたり最大16MB）のネイティブメモリも返却する
            // （使用中の接続には影響せず、次回アクセス時の再接続はローカルファイルでは数ms程度）
            _fileCacheRepository.ReleasePooledConnections();

            // LOHを含む全ヒープを圧縮して空き領域をOSへ返却する。ブロッキングGCなので
            // 予約が重ならないよう、ここへ来るのはアプリ全体で1本だけにしてある
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        });
    }
}
