using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>キャッシュからの即時表示と、ファイルシステムからのバックグラウンド更新に関する処理。</summary>
public partial class MainWindowViewModel
{
    /// <summary>DBキャッシュから即座に一覧を表示し、その後キャッシュ済みフォルダサイズの反映をリクエストする。</summary>
    private async Task LoadFromCacheAsync(string folderPath, int navigationVersion)
    {
        List<CachedFileSystemEntry> cachedEntries;

        try
        {
            // 除外パス追加直後は、次のスキャンで掃除されるまで除外対象がキャッシュに残っているため、表示前に弾く
            cachedEntries = await Task.Run(() =>
                _fileCacheRepository.GetEntriesByParentPath(folderPath)
                    .Where(x => !IsExcludedNormalizedPath(x.FullPath))
                    .ToList());
        }
        catch
        {
            return;
        }

        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
            {
                return;
            }

            UpdateCurrentDirectoryItems(cachedEntries.Select(ToViewModel));
            // キャッシュサイズ適用をリクエスト（統合）
            _folderSizeCoalescer.Request((folderPath, cachedEntries, navigationVersion));
        }, null);
    }

    /// <summary>実際のファイルシステムを読み取ってキャッシュを置き換え、画面へ反映する。</summary>
    private async Task RefreshFromFileSystemInBackground(string folderPath, int navigationVersion)
    {
        List<CachedFileSystemEntry> liveEntries;

        try
        {
            liveEntries = await Task.Run(() => ReadEntriesFromFileSystem(folderPath));
        }
        catch
        {
            // 列挙失敗（NASの瞬断等）時はキャッシュ由来の表示を維持し、キャッシュも書き換えない
            // （ここで続行すると空一覧の表示とキャッシュの空上書きにつながる）
            return;
        }

        try
        {
            await Task.Run(() => _fileCacheRepository.ReplaceEntriesByParentPath(folderPath, liveEntries));
        }
        catch
        {
            // キャッシュ保存失敗時でも画面更新は継続する
        }

        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
            {
                return;
            }

            UpdateCurrentDirectoryItems(liveEntries.Select(ToViewModel));
            // キャッシュサイズ適用をリクエスト（統合）
            _folderSizeCoalescer.Request((folderPath, liveEntries, navigationVersion));
        }, null);
    }

    /// <summary>フォルダ一覧に、キャッシュから集計した子フォルダ合計サイズを反映する（変化があった項目のみ更新）。</summary>
    private async Task ApplyCachedFolderSizesInBackground(string folderPath, IReadOnlyCollection<CachedFileSystemEntry> entries, int navigationVersion)
    {
        var folderEntries = entries
            .Where(x => x.IsFolder)
            .ToList();

        if (folderEntries.Count == 0)
        {
            return;
        }

        // キャッシュから取得するフォルダパスのみ抽出
        var folderPaths = folderEntries.Select(x => x.FullPath).ToList();

        Dictionary<string, long> cachedFolderSizes;
        try
        {
            cachedFolderSizes = await Task.Run(() => _fileCacheRepository.GetCachedFolderTotalSizes(folderPath, folderPaths));
        }
        catch
        {
            return;
        }

        // キャッシュが取得できなかったか、全フォルダがサイズ情報を持たない場合はスキップ
        if (cachedFolderSizes.Count == 0)
        {
            return;
        }

        // ナビゲーションの確認: 別のフォルダに移動していないか
        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !PathNormalizer.AreSame(CurrentPath, folderPath))
            {
                return;
            }

            // バイト数で比較して、実際に変わったフォルダのみ更新（不要な再描画を完全に防止）
            // フラット表示モード中は表示中の FileItems にフォルダ行が無いため、
            // 直下一覧の基準データ（_currentDirectoryItems）側にも反映しておく
            // （モード解除時に古いサイズが一瞬表示されるのを防ぐ）
            foreach (var folderEntry in folderEntries)
            {
                if (cachedFolderSizes.TryGetValue(folderEntry.FullPath, out var cachedSize))
                {
                    ApplyFolderSizeIfChanged(FileItems.FirstOrDefault(x => x.FullPath == folderEntry.FullPath), cachedSize);
                    ApplyFolderSizeIfChanged(_currentDirectoryItems.FirstOrDefault(x => x.FullPath == folderEntry.FullPath), cachedSize);
                }
            }
        }, null);
    }

    private static void ApplyFolderSizeIfChanged(FileItemViewModel? item, long cachedSize)
    {
        if (item != null && item.IsFolder && item.CachedSizeBytes != cachedSize)
        {
            // バイト数が実際に変わった場合のみ更新
            item.CachedSizeBytes = cachedSize;
            item.SizeText = cachedSize > 0 ? FileSizeFormatter.Format(cachedSize) : string.Empty;
        }
    }
}
