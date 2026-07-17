using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>現在フォルダ配下のキャッシュ検索に関する処理。</summary>
public partial class MainWindowViewModel
{
    /// <summary>
    /// 入力された検索語で検索をリクエストする（インクリメンタルサーチ）。
    /// 表示中の一覧はすぐには消さず、結果が届いた時点で差分更新する（入力の都度ちらつかせないため）。
    /// 現在フォルダが無効な場合は何もしない。
    /// </summary>
    private void RequestSearch(string query)
    {
        // 検索はキャッシュDBのみで完結するため、ライブのファイルシステム確認はしない
        // （切断中のNASへの Directory.Exists は、1キー入力ごとにUIスレッドを数秒〜数十秒ブロックしうる）
        if (string.IsNullOrWhiteSpace(CurrentPath))
        {
            return;
        }

        var normalizedQuery = query.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return;
        }

        var searchRootPath = CurrentPath;
        var searchVersion = Interlocked.Increment(ref _searchVersion);

        // All Filesモード中の検索はファイルのみを対象にする（フォルダは表示しない）。
        // 結果反映までにモードが切り替わっても古い結果を出さないよう、リクエスト時点の値を固定して渡す
        var filesOnly = IsFlatFileViewEnabled;

        // 検索リクエストを統合するキューへ委譲
        _searchCoalescer.Request((searchRootPath, normalizedQuery, searchVersion, filesOnly));
    }

    /// <summary>検索状態を解除し、現在の表示モード（通常一覧 or フラット表示）に戻す。SearchQueryが空になった際に呼ばれる。</summary>
    private void ClearSearch()
    {
        Interlocked.Increment(ref _searchVersion);

        if (IsFlatFileViewEnabled)
        {
            RequestFlatFileView();
            return;
        }

        ReplaceVisibleFileItems(_currentDirectoryItems);
    }

    /// <summary>キャッシュDBに対して検索を実行し、結果を画面へ反映する。</summary>
    private async Task SearchInBackground(string rootPath, string query, int searchVersion, bool filesOnly)
    {
        List<FileItemViewModel> cacheResults;

        try
        {
            // 除外パス追加直後は、次のスキャンで掃除されるまで除外対象がキャッシュに残っているため、表示前に弾く
            cacheResults = await Task.Run(() =>
                SearchCacheEntries(rootPath, query)
                    .Where(x => !(filesOnly && x.IsFolder))
                    .Where(x => !IsExcludedNormalizedPath(x.FullPath))
                    .Select(ToViewModel)
                    .ToList());
        }
        catch
        {
            cacheResults = new List<FileItemViewModel>();
        }

        if (searchVersion != Volatile.Read(ref _searchVersion)
            || !PathNormalizer.AreSame(CurrentPath, rootPath)
            || !string.Equals(SearchQuery.Trim(), query, StringComparison.Ordinal))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (searchVersion != Volatile.Read(ref _searchVersion)
                || !PathNormalizer.AreSame(CurrentPath, rootPath)
                || !string.Equals(SearchQuery.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            ReplaceVisibleFileItems(cacheResults);
        }, null);
    }

    /// <summary>検索起点が仮想「Roots」の場合は全ルートを横断検索し、それ以外は単一パス配下を検索する。</summary>
    private List<CachedFileSystemEntry> SearchCacheEntries(string rootPath, string query)
    {
        if (!AllRootsVirtualFolder.Matches(rootPath))
        {
            return _fileCacheRepository.SearchEntriesUnderPath(rootPath, query);
        }

        // ルート同士が入れ子（例: D:\ と D:\Sub）の場合に同一エントリが重複するため、FullPathで除去する
        return _rootPathsSnapshot
            .SelectMany(root => _fileCacheRepository.SearchEntriesUnderPath(root, query))
            .DistinctBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
