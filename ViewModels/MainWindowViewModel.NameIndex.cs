using System.Threading;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>
/// ファイル名索引（Plus機能）の組み立てと破棄。
/// 有効かどうかは設定（<see cref="AppSettings.IsNameIndexEnabled"/>）と購読状態の両方で決まる。
/// </summary>
public partial class MainWindowViewModel
{
    private readonly FileNameIndex _fileNameIndex;

    /// <summary>設定画面で「有効にする」が選ばれているか（購読していない間は索引を作らない）。</summary>
    private bool _isNameIndexEnabled;

    /// <summary>索引を組み立て中か（重ねて走らせないための番兵）。</summary>
    private int _isBuildingNameIndex;

    /// <summary>
    /// 組み立ての要求が来ているか。組み立て中に来た要求（スキャン完了など）を捨てず、
    /// 今の組み立てが終わったあとにもう一度組み立てるための印。
    /// </summary>
    private int _isNameIndexBuildRequested;

    /// <summary>ファイル名索引の設定が有効か（購読状態は問わない。設定画面へ返す値）。</summary>
    public bool GetNameIndexEnabled() => _isNameIndexEnabled;

    /// <summary>ファイル名索引が実際に使える状態か（設定・購読・組み立て完了のすべてが揃っているか）。</summary>
    public bool IsNameIndexReady => _fileNameIndex.IsReady;

    /// <summary>索引を使ってよい状態か。</summary>
    private bool ShouldUseNameIndex => _isNameIndexEnabled && _arePlusFeaturesEnabled;

    /// <summary>
    /// 設定・購読状態にあわせて索引を組み立て直す／捨てる。
    /// 起動時・設定変更時・購読状態の確定時・フルスキャン完了時に呼ぶ。
    /// </summary>
    private void ApplyNameIndexState()
    {
        if (!ShouldUseNameIndex)
        {
            _fileNameIndex.Clear();
            return;
        }

        RequestNameIndexBuild();
    }

    /// <summary>索引の組み立てをバックグラウンドで始める（150万件で1秒強かかるためUIスレッドでは走らせない）。</summary>
    private void RequestNameIndexBuild()
    {
        Volatile.Write(ref _isNameIndexBuildRequested, 1);

        // 組み立て中に重ねて要求されても、走らせるのは1本だけにする。
        // 組み立て中の要求は印だけ残し、今の組み立てを終えたあとに拾わせる
        // （捨てると、スキャン完了時の作り直しがスキャン前の内容のまま終わってしまう）
        if (Interlocked.CompareExchange(ref _isBuildingNameIndex, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(RunNameIndexBuildLoop);
    }

    /// <summary>要求の印が残っている限り組み立てを繰り返す（組み立て中に来た要求を取りこぼさない）。</summary>
    private void RunNameIndexBuildLoop()
    {
        while (true)
        {
            while (Interlocked.Exchange(ref _isNameIndexBuildRequested, 0) == 1)
            {
                // 待っている間に無効化・購読切れになっていれば組み立てない
                if (!ShouldUseNameIndex)
                {
                    _fileNameIndex.Clear();
                    continue;
                }

                try
                {
                    if (_fileNameIndex.Build())
                    {
                        // 並べ替え前の配列（150万件で80MB規模）はここで用済みになる。
                        // LOHに残ったままだとコミット済みのワーキングセットが積み上がるため回収を予約する
                        RequestMemoryTrim(_fileNameIndex.EntryCount);
                    }
                }
                catch
                {
                    // 組み立てに失敗しても検索はキャッシュDB経由で動くので、索引を持たない状態に倒す
                    _fileNameIndex.Clear();
                }
            }

            Volatile.Write(ref _isBuildingNameIndex, 0);

            // 番兵を下ろす直前に来た要求は、要求側の CompareExchange が失敗して誰にも拾われない。
            // 印が残っていれば自分で番兵を取り直して続ける
            if (Volatile.Read(ref _isNameIndexBuildRequested) == 0
                || Interlocked.CompareExchange(ref _isBuildingNameIndex, 1, 0) != 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// スキャンが完走せずに終わった（キャンセル・失敗）ときに呼ぶ。
    /// スキャン中の書き換えは索引へ控えてあるので通常は何もしないが、書き換えが多すぎて
    /// 索引を捨てていた場合は、完走時の作り直しが来ないためここで作り直す。
    /// </summary>
    public void OnScanInterrupted()
    {
        if (ShouldUseNameIndex && !_fileNameIndex.IsReady)
        {
            RequestNameIndexBuild();
        }
    }

    /// <summary>スキャンが書き換えた親フォルダを索引へ控える（索引の行IDが古くなり、結果から消えるのを防ぐ）。</summary>
    private void MarkScannedParentsChanged(IReadOnlyList<string> changedParentPaths)
    {
        foreach (var parentPath in changedParentPaths)
        {
            _fileNameIndex.MarkParentChanged(parentPath);
        }
    }

    /// <summary>設定画面からの切り替えを反映して保存する。</summary>
    public void SetNameIndexEnabled(bool isEnabled)
    {
        if (_isNameIndexEnabled == isEnabled)
        {
            return;
        }

        _isNameIndexEnabled = isEnabled;
        ApplyNameIndexState();
        SaveSettings();
    }

    /// <summary>
    /// 索引に使える状態のものを返す（使えないなら null）。
    /// 呼び出し側は null のときキャッシュDBへの検索に切り替える。
    /// </summary>
    private FileNameIndex? GetUsableNameIndex()
    {
        return ShouldUseNameIndex && _fileNameIndex.IsReady ? _fileNameIndex : null;
    }
}
