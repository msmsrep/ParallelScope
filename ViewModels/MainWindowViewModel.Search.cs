using System.IO;
using System.Threading;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>現在フォルダ配下のキャッシュ検索に関する処理。</summary>
public partial class MainWindowViewModel
{
    /// <summary>現在のフォルダを対象に SearchQuery で検索を実行する。</summary>
    public bool SearchCurrentPath()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) || !Directory.Exists(CurrentPath))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ClearSearch();
            return true;
        }

        var normalizedQuery = SearchQuery.Trim();
        var searchRootPath = CurrentPath;
        var searchVersion = Interlocked.Increment(ref _searchVersion);

        ReplaceVisibleFileItems(Array.Empty<FileItemViewModel>());
        // 検索リクエストを統合するキューへ委譲
        _searchCoalescer.Request((searchRootPath, normalizedQuery, searchVersion));
        return true;
    }

    /// <summary>検索状態を解除し、現在フォルダの通常一覧表示に戻す。</summary>
    public void ClearSearch()
    {
        Interlocked.Increment(ref _searchVersion);
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
