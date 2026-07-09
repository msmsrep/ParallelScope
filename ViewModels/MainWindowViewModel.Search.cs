using System.IO;
using System.Threading;
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
        if (string.IsNullOrWhiteSpace(CurrentPath) || !Directory.Exists(CurrentPath))
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

        // 検索リクエストを統合するキューへ委譲
        _searchCoalescer.Request((searchRootPath, normalizedQuery, searchVersion));
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
    private async Task SearchInBackground(string rootPath, string query, int searchVersion)
    {
        List<FileItemViewModel> cacheResults;

        try
        {
            cacheResults = await Task.Run(() =>
                _fileCacheRepository.SearchEntriesUnderPath(rootPath, query)
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
}
