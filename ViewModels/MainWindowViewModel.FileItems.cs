using System.Collections.ObjectModel;
using System.IO;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>FileItems（画面表示用コレクション）の差分更新とキャッシュエントリ→ViewModel変換。</summary>
public partial class MainWindowViewModel
{
    /// <summary>差分がこの件数を超えたらコレクションごと差し替える（All Filesモードの切り替え等で数万件の通知がUIスレッドを塞ぐのを防ぐ）。</summary>
    private const int BulkReplaceThreshold = 200;

    /// <summary>表示中のFileItemsを、差分（追加/削除/更新）だけを適用する形で置き換える（不要な再描画を防止）。</summary>
    private void ReplaceVisibleFileItems(IEnumerable<FileItemViewModel> items, bool forceBulkReplace = false)
    {
        var newItems = items.ToList();

        if (forceBulkReplace)
        {
            // モード切り替え時の大量データでは差分計算自体が高コストになるため、
            // 一括差し替えでUIスレッドのブロック時間を短縮する。
            FileItems = new ObservableCollection<FileItemViewModel>(newItems);
            return;
        }

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

        ApplyItemUpdates(itemsToUpdate);

        if (itemsToRemove.Count + itemsToAdd.Count > BulkReplaceThreshold)
        {
            // 1件ずつのAdd/RemoveはCollectionChanged通知が件数分発生し（Removeは1件ごとに線形探索も走る）、
            // 大量差分ではUIスレッドが数秒単位でブロックされる。コレクション差し替えなら再バインド1回で済み、
            // DataGridの行仮想化により生成されるのは可視行のみ。既存アイテムはサイズ表示等を保持するため
            // インスタンスを再利用する。
            FileItems = new ObservableCollection<FileItemViewModel>(
                newItems.Select(x => currentItemMap.TryGetValue(x.FullPath, out var existing) ? existing : x));
            return;
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
    }

    /// <summary>既存アイテムのプロパティを新規アイテムの情報で更新する。</summary>
    private static void ApplyItemUpdates(List<(FileItemViewModel existing, FileItemViewModel newItem)> itemsToUpdate)
    {
        foreach (var (existing, newItem) in itemsToUpdate)
        {
            existing.TypeText = newItem.TypeText;
            existing.ModifiedTime = newItem.ModifiedTime;
            existing.AttributesText = newItem.AttributesText;

            // 作成日時: 古いキャッシュ行には値が無い（空）ため、空で既存の値を上書きしない
            // （ライブ更新で一度表示された値がキャッシュ再読込で消えるのを防ぐ。作成日時は変化しない値なので保持で問題ない）
            if (!string.IsNullOrEmpty(newItem.CreatedTime))
            {
                existing.CreatedTime = newItem.CreatedTime;
            }

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

        // 検索中・フラット表示モード中は、直下一覧の更新で表示中の一覧を上書きしない
        if (string.IsNullOrWhiteSpace(SearchQuery) && !IsFlatFileViewEnabled)
        {
            ReplaceVisibleFileItems(_currentDirectoryItems);
        }
    }

    /// <summary>キャッシュエントリを画面表示用ViewModelへ変換する。</summary>
    private static FileItemViewModel ToViewModel(CachedFileSystemEntry entry)
    {
        var modifiedLocalTime = DateTime.SpecifyKind(entry.LastWriteTimeUtc, DateTimeKind.Utc).ToLocalTime();

        var item = entry.IsFolder
            ? new FileItemViewModel(entry.FullPath, entry.Name, modifiedLocalTime)
            : new FileItemViewModel(entry.FullPath, entry.Name, entry.SizeBytes ?? 0L, modifiedLocalTime);

        // 作成日時・属性は列追加前のキャッシュ行には無い（null）ため、次のスキャンで埋まるまで空表示になる
        if (entry.CreationTimeUtc is { } creationUtc)
        {
            item.CreatedTime = DateTime.SpecifyKind(creationUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        item.AttributesText = FormatAttributes(entry.Attributes);
        return item;
    }

    /// <summary>ファイル属性をエクスプローラー風の文字列（R/H/S/A）へ変換する。未取得（null）や該当なしは空。</summary>
    private static string FormatAttributes(int? attributes)
    {
        if (attributes is null)
        {
            return string.Empty;
        }

        var value = (FileAttributes)attributes.Value;
        Span<char> letters = stackalloc char[4];
        var count = 0;

        if (value.HasFlag(FileAttributes.ReadOnly)) letters[count++] = 'R';
        if (value.HasFlag(FileAttributes.Hidden)) letters[count++] = 'H';
        if (value.HasFlag(FileAttributes.System)) letters[count++] = 'S';
        if (value.HasFlag(FileAttributes.Archive)) letters[count++] = 'A';

        return count == 0 ? string.Empty : new string(letters[..count]);
    }
}
