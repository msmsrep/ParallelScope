using System.IO;
using System.Threading;

namespace ParallelScope.Utilities;

/// <summary>
/// UIスレッドから呼ぶための Directory.Exists の代替。
/// 切断されたNAS等への Directory.Exists はSMBのタイムアウト（数秒〜数十秒）までブロックすることがあるため、
/// 確認を別スレッドで行い、時間内に確定しなければ「存在する」とみなして返す。
/// 本アプリの表示はキャッシュ優先のため、楽観側に倒しても表示はキャッシュで賄え、
/// 実際に読めない場合はバックグラウンド更新・スキャン側の失敗処理が安全に受け止める。
/// 時間切れになったボリュームは <see cref="VolumeAvailabilityTracker"/> に控え、復帰するまでは
/// 問い合わせずに即答する（移動のたびに待たせない・見捨てた確認でスレッドを積み上げないため）。
/// </summary>
public static class DirectoryAvailabilityChecker
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>パスが存在するか確認する。時間内に確定しない場合は true（存在する扱い）を返す。</summary>
    public static bool ExistsOrTimedOut(string path)
    {
        var tracker = VolumeAvailabilityTracker.Shared;
        if (tracker.IsUnreachable(path))
        {
            return true;
        }

        var probe = StartProbe(() => Directory.Exists(path));
        if (probe.Wait(ProbeTimeout))
        {
            return probe.Result;
        }

        tracker.MarkUnreachable(path);
        return true;
    }

    /// <summary>
    /// パスが存在すれば、<paramref name="resolve"/> で表記を整えたパスを返す（存在しなければ null）。
    /// 時間内に確定しない場合と、ボリュームごと見えない（切断中）がキャッシュで開ける場合は、
    /// 入力のパスをそのまま返す（存在する扱い）。
    /// 表記の整え（ファイルシステムへの問い合わせを伴う）も存在確認と同じくバックグラウンドで行う。
    /// </summary>
    /// <param name="resolve">存在が確認できたパスの表記を整える処理。例外を投げないこと。</param>
    /// <param name="canOpenOffline">
    /// ボリュームがつながらないときに、キャッシュを頼りに開いてよいパスか（キャッシュに載っている等）。
    /// バックグラウンドから呼ばれる。例外を投げないこと。
    /// </param>
    public static string? ResolveExistingOrTimedOut(string path, Func<string, string> resolve, Func<string, bool> canOpenOffline)
    {
        var tracker = VolumeAvailabilityTracker.Shared;
        if (tracker.IsUnreachable(path))
        {
            // つながらないと分かっているボリュームには問い合わせない。表記の整えも行わない
            // （キャッシュの表記で開かれるので、ほとんどの場合はそろっている）
            return path;
        }

        var probe = StartProbe(() =>
        {
            if (Directory.Exists(path))
            {
                return resolve(path);
            }

            // 見えないのがフォルダだけか、ボリュームごとか（＝切断中）を分ける。
            // 切断中でもキャッシュに載っているフォルダはキャッシュの内容で閲覧できるようにする
            if (!tracker.CheckVolume(path))
            {
                return canOpenOffline(path) ? path : null;
            }

            return null;
        });

        if (probe.Wait(ProbeTimeout))
        {
            return probe.Result;
        }

        tracker.MarkUnreachable(path);
        return path;
    }

    // 確認はスレッドプールを使わない —— 時間切れで見捨てた確認はSMBのタイムアウトまでスレッドを握り続けるため、
    // プールのスレッドで行うと切断中にアプリ内の Task.Run 全体が遅れる
    private static Task<T> StartProbe<T>(Func<T> probe)
    {
        return Task.Factory.StartNew(probe, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
