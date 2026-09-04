using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>「フォルダ以下のすべてのファイルを表示する」モード（再帰的なフラット表示）に関する処理。</summary>
public partial class BrowserTabViewModel
{
    /// <summary>現在フォルダ配下の全ファイルをキャッシュから再帰的に取得するようリクエストする。</summary>
    private void RequestFlatFileView()
    {
        // フラット表示はキャッシュDBのみで完結するため、ライブのファイルシステム確認はしない
        // （切断中のNASへの Directory.Exists はUIスレッドを数秒〜数十秒ブロックしうる）
        if (string.IsNullOrWhiteSpace(CurrentPath))
        {
            return;
        }

        var folderPath = CurrentPath;
        var flatViewVersion = Interlocked.Increment(ref _flatViewVersion);

        // フラット表示リクエストを統合するキューへ委譲
        _flatViewCoalescer.Request((folderPath, flatViewVersion));
    }

    /// <summary>取得の途中経過を最初に画面へ出す件数。</summary>
    private const int FlatViewFirstBatchSize = 2_000;

    /// <summary>途中経過を出すたびに、次に出すまでの件数をこの倍率で広げる（件数が増えるほど間引く）。</summary>
    private const int FlatViewBatchGrowthFactor = 8;

    /// <summary>キャッシュDBから配下の全ファイルを取得し、結果を画面へ反映する。</summary>
    /// <remarks>
    /// 全件が揃うまで待たず、貯まった分から順に表示する（段階表示）。ドライブ直下のように
    /// 百万件規模になる場所では取得だけで数秒かかり、その間ずっと古い一覧のままになるため。
    /// 表示のたびにコレクションを差し替える（＝一覧の先頭へ戻る）ので、件数が増えるほど
    /// 表示の間隔を広げ、回数を数回に抑えている。
    /// </remarks>
    private async Task ApplyFlatFileView(string folderPath, int flatViewVersion)
    {
        await Task.Run(() =>
        {
            var items = new List<FileItemViewModel>();
            var nextPublishCount = FlatViewFirstBatchSize;

            try
            {
                // 除外パス追加直後は、次のスキャンで掃除されるまで除外対象がキャッシュに残っているため、表示前に弾く
                foreach (var item in ToViewModels(
                    GetFlatViewFiles(folderPath)
                        .Where(x => !_host.IsExcludedNormalizedPath(x.FullPath))))
                {
                    items.Add(item);

                    if (items.Count < nextPublishCount)
                    {
                        continue;
                    }

                    if (!IsFlatFileViewResultStillValid(folderPath, flatViewVersion))
                    {
                        return;
                    }

                    // 途中経過はこの後も追記が続くため、渡すのは複製
                    PublishFlatFileView(folderPath, flatViewVersion, items.ToList());
                    nextPublishCount = items.Count * FlatViewBatchGrowthFactor;
                }
            }
            catch
            {
                // 途中で失敗しても、そこまでに読めた分は表示する
                // （読み出しの開始時点で失敗した場合は空一覧になり、従来と同じ）
            }

            if (!IsFlatFileViewResultStillValid(folderPath, flatViewVersion))
            {
                return;
            }

            PublishFlatFileView(folderPath, flatViewVersion, items);
        });
    }

    /// <summary>フラット表示の取得結果（途中経過を含む）を画面へ反映する。</summary>
    private void PublishFlatFileView(string folderPath, int flatViewVersion, List<FileItemViewModel> items)
    {
        _host.UiContext.Post(_ =>
        {
            if (!IsFlatFileViewResultStillValid(folderPath, flatViewVersion))
            {
                return;
            }

            ReplaceVisibleFileItems(items, forceBulkReplace: true);
        }, null);
    }

    /// <summary>起点が仮想ノードの場合は対象フォルダ群を横断し、それ以外は単一パス配下の全ファイルを列挙する。</summary>
    /// <remarks>数十万件規模のため List 化せず、リポジトリの逐次読み出しをそのまま流す（ピークメモリ削減）。</remarks>
    private IEnumerable<CachedFileSystemEntry> GetFlatViewFiles(string folderPath)
    {
        var traversalPaths = _host.GetTraversalPaths(folderPath);
        if (traversalPaths.Count == 1)
        {
            return _host.FileCacheRepository.EnumerateFilesUnderPath(traversalPaths[0]);
        }

        var files = traversalPaths
            .SelectMany(root => _host.FileCacheRepository.EnumerateFilesUnderPath(root));

        // 対象同士が入れ子（例: D:\ と D:\Sub）の場合のみ同一エントリが重複するため、その場合だけ
        // FullPathで除去する（通常構成で全ファイル分の FullPath 文字列を判定セットに同時保持しないため）
        return HasOverlappingPaths(traversalPaths)
            ? files.DistinctBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            : files;
    }

    /// <summary>取得結果が届いた時点でもまだ表示すべき状態か（フォルダ移動・モード解除・検索開始が起きていないか）を確認する。</summary>
    private bool IsFlatFileViewResultStillValid(string folderPath, int flatViewVersion)
    {
        return flatViewVersion == Volatile.Read(ref _flatViewVersion)
            && IsFlatFileViewEnabled
            && string.IsNullOrWhiteSpace(SearchQuery)
            && PathNormalizer.AreSame(CurrentPath, folderPath);
    }
}
