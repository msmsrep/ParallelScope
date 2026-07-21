using System.IO;
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

    /// <summary>仮想「Folders」表示用に、各ルートをフォルダ行として一覧化する（合計サイズはキャッシュから集計）。</summary>
    private async Task LoadAllRootsListingAsync(int navigationVersion)
    {
        var rootPaths = _rootPathsSnapshot;

        List<FileItemViewModel> rootItems;
        try
        {
            rootItems = await Task.Run(() =>
            {
                var cachedTotalSizes = _fileCacheRepository.GetCachedTotalSizesUnderPaths(rootPaths);
                return rootPaths
                    .Select(rootPath =>
                    {
                        long? totalSize = cachedTotalSizes.TryGetValue(rootPath, out var size) && size > 0 ? size : null;
                        var name = Path.GetFileName(rootPath);
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = rootPath;
                        }

                        // FullPath は Location + Name から再構成されるため、Path.GetDirectoryName では
                        // 復元できないルート（ドライブルートやUNC共有ルート）でも rootPath に戻るよう、
                        // rootPath から Name を除いた前半をそのまま Location として渡す。
                        // 更新日時はライブFSアクセスなしでは取得できない（切断中のNASでブロックしうる）ため
                        // MinValue（空表示）にする
                        return new FileItemViewModel(
                            rootPath,
                            name,
                            DateTime.MinValue,
                            totalSize,
                            location: rootPath[..^name.Length]);
                    })
                    .ToList();
            });
        }
        catch
        {
            return;
        }

        if (navigationVersion != Volatile.Read(ref _navigationVersion) || !AllRootsVirtualFolder.Matches(CurrentPath))
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            if (navigationVersion != Volatile.Read(ref _navigationVersion) || !AllRootsVirtualFolder.Matches(CurrentPath))
            {
                return;
            }

            UpdateCurrentDirectoryItems(rootItems);
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

            // バイト数で比較して、実際に変わったフォルダのみ更新（不要な再描画を完全に防止）。
            // フラット表示モード中は表示中の FileItems にフォルダ行が無いため、
            // 直下一覧の基準データ（_currentDirectoryItems）側にも反映しておく
            // （モード解除時に古いサイズが一瞬表示されるのを防ぐ）。
            // 対象は folderPath 直下のフォルダ行のみなので名前で一意に引ける
            // （FullPath は保持されず都度生成のため、FullPath比較の線形探索より速く、文字列生成も避けられる）
            var visibleFolderByName = BuildDirectChildFolderLookup(FileItems, folderPath);
            var baseFolderByName = BuildDirectChildFolderLookup(_currentDirectoryItems, folderPath);

            foreach (var folderEntry in folderEntries)
            {
                if (cachedFolderSizes.TryGetValue(folderEntry.FullPath, out var cachedSize))
                {
                    ApplyFolderSizeIfChanged(visibleFolderByName.GetValueOrDefault(folderEntry.Name), cachedSize);
                    ApplyFolderSizeIfChanged(baseFolderByName.GetValueOrDefault(folderEntry.Name), cachedSize);
                }
            }
        }, null);
    }

    /// <summary>parentPath 直下のフォルダ行を、フォルダ名で引けるようにマップ化する。</summary>
    private static Dictionary<string, FileItemViewModel> BuildDirectChildFolderLookup(
        IEnumerable<FileItemViewModel> items, string parentPath)
    {
        var result = new Dictionary<string, FileItemViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.IsFolder && string.Equals(item.Location, parentPath, StringComparison.OrdinalIgnoreCase))
            {
                result.TryAdd(item.Name, item);
            }
        }

        return result;
    }

    private static void ApplyFolderSizeIfChanged(FileItemViewModel? item, long cachedSize)
    {
        // バイト数が実際に変わった場合のみ更新。0は「サイズ不明」と同じ空表示（null）に寄せる
        if (item != null && item.IsFolder && (item.SizeBytes ?? 0) != cachedSize)
        {
            item.SizeBytes = cachedSize > 0 ? cachedSize : null;
        }
    }
}
