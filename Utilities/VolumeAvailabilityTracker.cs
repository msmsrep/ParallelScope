using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace ParallelScope.Utilities;

/// <summary>
/// 応答しないボリューム（切断中のNASの共有・ネットワークドライブなど）を控えておき、
/// 復帰するまでファイルシステムへの問い合わせを省かせるための台帳。
/// </summary>
/// <remarks>
/// 切断中の共有への問い合わせはSMBのタイムアウト（数秒〜数十秒）までスレッドを握る。
/// 一度つながらないと分かったボリュームに移動のたびに問い合わせると、そのたびにUIスレッドの待ちと
/// スレッドプールの枯渇（見捨てた問い合わせがスレッドを握り続ける）が起きるため、
/// 復帰の確認はボリュームごとに1本の専用スレッドだけで行い、それ以外の呼び出し側は台帳を見るだけにする。
/// </remarks>
public sealed class VolumeAvailabilityTracker
{
    private static readonly TimeSpan DefaultRecheckInterval = TimeSpan.FromSeconds(15);

    /// <summary>アプリ全体で共有する台帳（ボリュームの到達性はプロセス全体で1つの事実のため）。</summary>
    public static VolumeAvailabilityTracker Shared { get; } = new(Directory.Exists, DefaultRecheckInterval);

    private readonly Func<string, bool> _probe;
    private readonly TimeSpan _recheckInterval;
    // キー: ボリュームのルート。値は使わない（集合として使う）
    private readonly ConcurrentDictionary<string, byte> _unreachableVolumes = new(StringComparer.OrdinalIgnoreCase);
    // 失敗報告を受けて確認中のボリューム（同じボリュームへの確認を重ねて走らせない）
    private readonly ConcurrentDictionary<string, byte> _checkingVolumes = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="probe">ボリュームのルートが見えるかを返す（ブロックしうる。専用スレッドからだけ呼ぶ）。</param>
    /// <param name="recheckInterval">つながらないボリュームの復帰を確かめる間隔。</param>
    public VolumeAvailabilityTracker(Func<string, bool> probe, TimeSpan recheckInterval)
    {
        _probe = probe;
        _recheckInterval = recheckInterval;
    }

    /// <summary>
    /// 控えていたボリュームが再び見えるようになったときに、そのボリュームのルートを渡して通知する
    /// （確認用のスレッドから呼ばれる）。
    /// </summary>
    public event Action<string>? VolumeRestored;

    /// <summary>パスの属するボリュームのルートを返す（相対パスや仮想パスなど、ボリュームを持たなければ null）。</summary>
    public static string? GetVolumeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || VirtualFolders.IsVirtual(path))
        {
            return null;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root))
            {
                return null;
            }

            return root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>パスの属するボリュームが、つながらないものとして控えられているか。</summary>
    public bool IsUnreachable(string? path)
    {
        return GetVolumeRoot(path) is { } root && _unreachableVolumes.ContainsKey(root);
    }

    /// <summary>パスの属するボリュームを、つながらないものとして控える（復帰の確認を始める）。</summary>
    public void MarkUnreachable(string? path)
    {
        if (GetVolumeRoot(path) is not { } root || !_unreachableVolumes.TryAdd(root, 0))
        {
            return;
        }

        // 確認はスレッドプールを使わない —— 切断中は1回の確認がSMBのタイムアウトまで戻らないため
        _ = Task.Factory.StartNew(
            () => WatchUntilRestored(root),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>
    /// パスの読み取りに失敗したことを知らせる。ボリュームごと見えなくなっているかをバックグラウンドで確かめ、
    /// 見えなければ控える（フォルダ単位の失敗＝アクセス権など、ボリュームが見えている場合は何もしない）。
    /// </summary>
    public void ReportFailure(string? path)
    {
        if (GetVolumeRoot(path) is not { } root
            || _unreachableVolumes.ContainsKey(root)
            || !_checkingVolumes.TryAdd(root, 0))
        {
            return;
        }

        _ = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    if (!SafeProbe(root))
                    {
                        MarkUnreachable(root);
                    }
                }
                finally
                {
                    _checkingVolumes.TryRemove(root, out _);
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>
    /// ボリュームが見えるかをその場で確かめる（ブロックしうる。バックグラウンドから呼ぶ）。
    /// 見えなければ控え、見えれば控えを外す。
    /// </summary>
    public bool CheckVolume(string? path)
    {
        if (GetVolumeRoot(path) is not { } root)
        {
            return true;
        }

        if (SafeProbe(root))
        {
            return true;
        }

        MarkUnreachable(root);
        return false;
    }

    private void WatchUntilRestored(string root)
    {
        while (true)
        {
            Thread.Sleep(_recheckInterval);

            if (!_unreachableVolumes.ContainsKey(root))
            {
                return;
            }

            if (!SafeProbe(root))
            {
                continue;
            }

            if (_unreachableVolumes.TryRemove(root, out _))
            {
                try
                {
                    VolumeRestored?.Invoke(root);
                }
                catch
                {
                    // 通知先の失敗で確認スレッドを落とさない
                }
            }

            return;
        }
    }

    private bool SafeProbe(string root)
    {
        try
        {
            return _probe(root);
        }
        catch
        {
            return false;
        }
    }
}
