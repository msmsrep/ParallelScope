using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>FileItems（画面表示用コレクション）の差分更新とキャッシュエントリ→ViewModel変換。</summary>
public partial class MainWindowViewModel
{
    /// <summary>表示中のFileItemsを、差分（追加/削除/更新）だけを適用する形で置き換える（不要な再描画を防止）。</summary>
    private void ReplaceVisibleFileItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();
        var currentItems = FileItems.ToList();

        // 既存アイテムをパスでマップ
        var currentItemMap = currentItems.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);

        // 削除するアイテムを特定（パスで比較）
        var newItemPaths = new HashSet<string>(newItems.Select(x => x.FullPath), StringComparer.OrdinalIgnoreCase);
        var itemsToRemove = currentItems
            .Where(x => !newItemPaths.Contains(x.FullPath))
            .ToList();

        // 追加するアイテムと更新するアイテムを特定
        var itemsToAdd = new List<FileItemViewModel>();
        var itemsToUpdate = new List<(FileItemViewModel existing, FileItemViewModel newItem)>();

        foreach (var newItem in newItems)
        {
            if (currentItemMap.TryGetValue(newItem.FullPath, out var existingItem))
            {
                itemsToUpdate.Add((existingItem, newItem));
            }
            else
            {
                itemsToAdd.Add(newItem);
            }
        }

        // 削除
        foreach (var item in itemsToRemove)
        {
            FileItems.Remove(item);
        }

        // 追加
        foreach (var item in itemsToAdd)
        {
            FileItems.Add(item);
        }

        // 更新（既存アイテムのプロパティを新規アイテムの情報で更新）
        foreach (var (existing, newItem) in itemsToUpdate)
        {
            existing.TypeText = newItem.TypeText;
            existing.ModifiedTime = newItem.ModifiedTime;

            // サイズ情報：新規アイテムが空で既存アイテムが有る場合は既存値を保持
            if (!string.IsNullOrEmpty(newItem.SizeText))
            {
                existing.SizeText = newItem.SizeText;
                existing.CachedSizeBytes = newItem.CachedSizeBytes;
            }
            // 新規アイテムが空で既存アイテムが有る場合は既存値を保持（キャッシュから取得したサイズ）
            else if (string.IsNullOrEmpty(newItem.SizeText) && !string.IsNullOrEmpty(existing.SizeText) && existing.CachedSizeBytes > 0)
            {
                // 既存のサイズ情報を保持
            }
        }
    }

    /// <summary>現在フォルダの全件（検索対象外の基準データ）を更新し、検索中でなければ表示にも反映する。</summary>
    private void UpdateCurrentDirectoryItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();

        // 既存のアイテムからサイズ情報を引き継ぐ（ライブデータの再取得時にキャッシュサイズが失われるのを防止）
        var currentItemMap = _currentDirectoryItems.ToDictionary(x => x.FullPath, StringComparer.OrdinalIgnoreCase);
        foreach (var newItem in newItems)
        {
            if (currentItemMap.TryGetValue(newItem.FullPath, out var existingItem))
            {
                newItem.CachedSizeBytes = existingItem.CachedSizeBytes;
                // SizeText も引き継ぐ（キャッシュサイズから計算された値）
                if (!string.IsNullOrEmpty(existingItem.SizeText))
                {
                    newItem.SizeText = existingItem.SizeText;
                }
            }
        }

        _currentDirectoryItems = newItems;

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ReplaceVisibleFileItems(_currentDirectoryItems);
        }
    }

    /// <summary>キャッシュエントリを画面表示用ViewModelへ変換する。</summary>
    private static FileItemViewModel ToViewModel(CachedFileSystemEntry entry)
    {
        var modifiedLocalTime = DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc).ToLocalTime();

        if (entry.IsFolder)
        {
            return new FileItemViewModel(entry.FullPath, entry.Name, modifiedLocalTime);
        }

        return new FileItemViewModel(entry.FullPath, entry.Name, entry.SizeBytes ?? 0L, modifiedLocalTime);
    }
}
