using System.IO;

namespace ParallelScope.Utilities;

/// <summary>
/// UIスレッドから呼ぶための Directory.Exists の代替。
/// 切断されたNAS等への Directory.Exists はSMBのタイムアウト（数秒〜数十秒）までブロックすることがあるため、
/// 確認を別スレッドで行い、時間内に確定しなければ「存在する」とみなして返す。
/// 本アプリの表示はキャッシュ優先のため、楽観側に倒しても表示はキャッシュで賄え、
/// 実際に読めない場合はバックグラウンド更新・スキャン側の失敗処理が安全に受け止める。
/// </summary>
public static class DirectoryAvailabilityChecker
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <summary>パスが存在するか確認する。時間内に確定しない場合は true（存在する扱い）を返す。</summary>
    public static bool ExistsOrTimedOut(string path)
    {
        var probe = Task.Run(() => Directory.Exists(path));
        return probe.Wait(ProbeTimeout) ? probe.Result : true;
    }
}
