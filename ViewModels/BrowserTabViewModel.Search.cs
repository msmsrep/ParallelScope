using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>現在フォルダ配下のキャッシュ検索に関する処理。</summary>
public partial class BrowserTabViewModel
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
        ForgetCompletedSearch();

        if (IsFlatFileViewEnabled)
        {
            RequestFlatFileView();
            return;
        }

        ReplaceVisibleFileItems(_currentDirectoryItems);
    }

    /// <summary>取得を続けてよいか（検索語の変更・フォルダ移動が起きていないか）を確認する間隔（件数）。</summary>
    private const int SearchAbortCheckInterval = 1_000;

    /// <summary>
    /// 直前に完了した検索の、表示していた結果一式。検索語を足しただけならここから絞り込めるため、
    /// キャッシュDBを引き直さずに済む（ドライブ直下では1回あたり数百msかかる）。
    /// 参照ごと差し替えるだけなので、UIスレッドとバックグラウンドの間はVolatileの読み書きで足りる。
    /// </summary>
    private sealed record CompletedSearch(string RootPath, string Query, bool FilesOnly, List<FileItemViewModel> Results);

    private CompletedSearch? _completedSearch;

    /// <summary>検索結果の使い回しをやめる（キャッシュが更新された・検索を終えた・一覧を手放した）。</summary>
    private void ForgetCompletedSearch()
    {
        Volatile.Write(ref _completedSearch, null);
    }

    /// <summary>
    /// 直前の検索結果から絞り込めるなら、その結果を返す（引き直しが必要なら null）。
    /// `%repo%` に一致する名前は必ず `%rep%` にも一致するので、検索語が前回の検索語を含んでいれば
    /// 新しい結果は必ず前回の結果の部分集合になる。
    /// </summary>
    private List<FileItemViewModel>? TryNarrowCompletedSearch(string rootPath, string query, bool filesOnly)
    {
        var completed = Volatile.Read(ref _completedSearch);

        // 検索語の包含判定は大文字小文字をそのまま見る（Ordinal）。
        // ここを寛容にすると、畳み方の違いでDBを引き直したときと結果が変わりうるため。
        // 打ち足していく通常の操作では前回の検索語がそのまま前方に残るので、これで十分効く
        if (completed is null
            || completed.FilesOnly != filesOnly
            || !query.Contains(completed.Query, StringComparison.Ordinal)
            || !PathNormalizer.AreSame(completed.RootPath, rootPath))
        {
            return null;
        }

        var narrowed = new List<FileItemViewModel>();
        foreach (var item in completed.Results)
        {
            if (NameSearchMatcher.Contains(item.Name, query))
            {
                narrowed.Add(item);
            }
        }

        return narrowed;
    }

    /// <summary>キャッシュDBに対して検索を実行し、結果を画面へ反映する。</summary>
    private async Task SearchInBackground(string rootPath, string query, int searchVersion, bool filesOnly)
    {
        // 直前の結果から絞り込めるならDBは引かない。ここはキューが空のとき要求元（UIスレッド）から
        // そのまま同期実行されるため、必ずバックグラウンドへ逃がす
        // （1文字の検索語の結果は数十万件あり、その絞り込みをUIスレッドでやると入力が引っかかる）
        if (await Task.Run(() => TryNarrowCompletedSearch(rootPath, query, filesOnly)) is { } narrowedResults)
        {
            PublishSearchResults(rootPath, query, searchVersion, filesOnly, narrowedResults);
            return;
        }

        List<FileItemViewModel> cacheResults;

        try
        {
            cacheResults = await Task.Run(() =>
            {
                var results = new List<FileItemViewModel>();

                // 除外パス追加直後は、次のスキャンで掃除されるまで除外対象がキャッシュに残っているため、表示前に弾く
                foreach (var item in ToViewModels(
                    SearchCacheEntries(rootPath, query)
                        .Where(x => !(filesOnly && x.IsFolder))
                        .Where(x => !_host.IsExcludedNormalizedPath(x.FullPath))))
                {
                    results.Add(item);

                    // 1文字の検索語では数十万件ヒットしうる一方、インクリメンタルサーチは
                    // 1キー入力ごとに要求が来て、キューは直列実行される。打ち切らないと
                    // 次の入力の検索が、用済みになった列挙の後ろで待たされてしまう
                    if (results.Count % SearchAbortCheckInterval == 0
                        && !IsSearchResultStillValid(rootPath, query, searchVersion))
                    {
                        // 打ち切った時点の状態は下の確認でも同じく不一致になるので、そのまま返して弾かせる
                        // （検索バージョンは要求のたびに増えるだけで、一度ずれたら戻らない）
                        return results;
                    }
                }

                return results;
            });
        }
        catch
        {
            cacheResults = new List<FileItemViewModel>();
        }

        PublishSearchResults(rootPath, query, searchVersion, filesOnly, cacheResults);
    }

    /// <summary>検索結果を画面へ反映し、次の入力で絞り込めるよう控える。</summary>
    private void PublishSearchResults(string rootPath, string query, int searchVersion, bool filesOnly, List<FileItemViewModel> results)
    {
        if (!IsSearchResultStillValid(rootPath, query, searchVersion))
        {
            return;
        }

        _host.UiContext.Post(_ =>
        {
            if (!IsSearchResultStillValid(rootPath, query, searchVersion))
            {
                return;
            }

            ReplaceVisibleFileItems(results);

            // 控えるのは実際に表示したインスタンス。ReplaceVisibleFileItems は既存の行を
            // 使い回すため、渡した results をそのまま控えると同じ行を二重に抱えることになる
            Volatile.Write(ref _completedSearch, new CompletedSearch(rootPath, query, filesOnly, FileItems.ToList()));
        }, null);
    }

    /// <summary>結果が届いた時点でもまだ表示すべき状態か（検索語の変更・フォルダ移動が起きていないか）を確認する。</summary>
    private bool IsSearchResultStillValid(string rootPath, string query, int searchVersion)
    {
        return searchVersion == Volatile.Read(ref _searchVersion)
            && PathNormalizer.AreSame(CurrentPath, rootPath)
            && string.Equals(SearchQuery.Trim(), query, StringComparison.Ordinal);
    }

    /// <summary>検索起点が仮想ノードの場合は対象フォルダ群を横断検索し、それ以外は単一パス配下を検索する。</summary>
    /// <remarks>数十万件ヒットしうるため List 化せず逐次列挙で返し、呼び出し側でViewModelへ直接変換させる（ピークメモリ削減）。</remarks>
    private IEnumerable<CachedFileSystemEntry> SearchCacheEntries(string rootPath, string query)
    {
        var traversalPaths = _host.GetTraversalPaths(rootPath);
        if (traversalPaths.Count == 1)
        {
            return _host.FileCacheRepository.EnumerateSearchEntriesUnderPath(traversalPaths[0], query);
        }

        var results = traversalPaths
            .SelectMany(root => _host.FileCacheRepository.EnumerateSearchEntriesUnderPath(root, query));

        // 対象同士が入れ子（例: D:\ と D:\Sub）の場合のみ同一エントリが重複するため、その場合だけ
        // FullPathで除去する（通常構成でヒット全件分の FullPath 文字列を判定セットに同時保持しないため）
        if (HasOverlappingPaths(traversalPaths))
        {
            results = results.DistinctBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
        }

        return results
            .OrderByDescending(x => x.IsFolder)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase);
    }
}
