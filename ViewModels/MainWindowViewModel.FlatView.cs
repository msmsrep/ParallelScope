using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>「フォルダ以下のすべてのファイルを表示する」モード（再帰的なフラット表示）に関する処理。</summary>
public partial class MainWindowViewModel
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

    /// <summary>キャッシュDBから配下の全ファイルを取得し、結果を画面へ反映する。</summary>
    private async Task ApplyFlatFileView(string folderPath, int flatViewVersion)
    {
        List<FileItemViewModel> results;

        try
        {
            // 除外パス追加直後は、次のスキャンで掃除されるまで除外対象がキャッシュに残っているため、表示前に弾く
            results = await Task.Run(() =>
                GetFlatViewFiles(folderPath)
                    .Where(x => !IsExcludedNormalizedPath(x.FullPath))
                    .Select(ToViewModel)
                    .ToList());
        }
        catch
        {
            results = new List<FileItemViewModel>();
        }

        if (!IsFlatFileViewResultStillValid(folderPath, flatViewVersion))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (!IsFlatFileViewResultStillValid(folderPath, flatViewVersion))
            {
                return;
            }

            ReplaceVisibleFileItems(results, forceBulkReplace: true);
        }, null);
    }

    /// <summary>起点が仮想「Roots」の場合は全ルート横断で、それ以外は単一パス配下の全ファイルを取得する。</summary>
    private List<CachedFileSystemEntry> GetFlatViewFiles(string folderPath)
    {
        if (!AllRootsVirtualFolder.Matches(folderPath))
        {
            return _fileCacheRepository.GetFilesUnderPath(folderPath);
        }

        // ルート同士が入れ子（例: D:\ と D:\Sub）の場合に同一エントリが重複するため、FullPathで除去する
        return _rootPathsSnapshot
            .SelectMany(root => _fileCacheRepository.GetFilesUnderPath(root))
            .DistinctBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
