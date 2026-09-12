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
        // 組み立て中に重ねて要求されても、走らせるのは1本だけにする
        // （終わったあとの要求は次の呼び出しで拾われる）
        if (Interlocked.CompareExchange(ref _isBuildingNameIndex, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _fileNameIndex.Build();

                // 並べ替え前の配列（150万件で80MB規模）はここで用済みになる。
                // LOHに残ったままだとコミット済みのワーキングセットが積み上がるため回収を予約する
                RequestMemoryTrim(_fileNameIndex.EntryCount);
            }
            catch
            {
                // 組み立てに失敗しても検索はキャッシュDB経由で動くので、索引を持たない状態に倒す
                _fileNameIndex.Clear();
            }
            finally
            {
                Volatile.Write(ref _isBuildingNameIndex, 0);
            }
        });
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
