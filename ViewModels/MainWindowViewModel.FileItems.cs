using System.Collections.ObjectModel;
using System.IO;
using ParallelScope.Data;

namespace ParallelScope.ViewModels;

/// <summary>FileItems（画面表示用コレクション）の差分更新とキャッシュエントリ→ViewModel変換。</summary>
public partial class MainWindowViewModel
{
    /// <summary>差分がこの件数を超えたらコレクションごと差し替える（All Filesモードの切り替え等で数万件の通知がUIスレッドを塞ぐのを防ぐ）。</summary>
    private const int BulkReplaceThreshold = 200;

    /// <summary>
    /// アイテムの同一性判定キー。FullPath は保持せず Location + Name から都度生成するため、
    /// FullPath 文字列をキーにすると差分計算のたびに全件分のパス文字列生成が走る。
    /// 構成要素のペアをそのままキーにすれば追加の文字列生成なしで同じ判定になる。
    /// </summary>
    private static (string Location, string Name) PathKeyOf(FileItemViewModel item) => (item.Location, item.Name);

    private sealed class PathKeyComparer : IEqualityComparer<(string Location, string Name)>
    {
        public static readonly PathKeyComparer Instance = new();

        public bool Equals((string Location, string Name) x, (string Location, string Name) y)
        {
            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Location, y.Location, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((string Location, string Name) key)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Location),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Name));
        }
    }

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
        var currentItemMap = currentItems.ToDictionary(PathKeyOf, PathKeyComparer.Instance);

        // 削除するアイテムを特定（パスで比較）
        var newItemPaths = newItems.Select(PathKeyOf).ToHashSet(PathKeyComparer.Instance);
        var itemsToRemove = currentItems
            .Where(x => !newItemPaths.Contains(PathKeyOf(x)))
            .ToList();

        // 追加するアイテムと更新するアイテムを特定
        var itemsToAdd = new List<FileItemViewModel>();
        var itemsToUpdate = new List<(FileItemViewModel existing, FileItemViewModel newItem)>();

        foreach (var newItem in newItems)
        {
            if (currentItemMap.TryGetValue(PathKeyOf(newItem), out var existingItem))
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
                newItems.Select(x => currentItemMap.TryGetValue(PathKeyOf(x), out var existing) ? existing : x));
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
            existing.ModifiedAt = newItem.ModifiedAt;
            existing.AttributesText = newItem.AttributesText;

            // 作成日時: 古いキャッシュ行には値が無い（null）ため、nullで既存の値を上書きしない
            // （ライブ更新で一度表示された値がキャッシュ再読込で消えるのを防ぐ。作成日時は変化しない値なので保持で問題ない）
            if (newItem.CreatedAt is { } createdAt)
            {
                existing.CreatedAt = createdAt;
            }

            // サイズ: 新規アイテムが未取得（null。フォルダのキャッシュ集計が無い場合等）なら、
            // キャッシュ集計由来の既存値を保持する
            if (newItem.SizeBytes is { } sizeBytes)
            {
                existing.SizeBytes = sizeBytes;
            }
        }
    }

    /// <summary>現在フォルダの全件（検索対象外の基準データ）を更新し、検索中でなければ表示にも反映する。</summary>
    private void UpdateCurrentDirectoryItems(IEnumerable<FileItemViewModel> items)
    {
        var newItems = items.ToList();

        // 既存のアイテムからフォルダ合計サイズを引き継ぐ（キャッシュ集計でしか得られない値のため、
        // ライブデータの再取得時に失われるのを防止）。ファイルはライブ列挙の最新サイズをそのまま使う
        var currentItemMap = _currentDirectoryItems.ToDictionary(PathKeyOf, PathKeyComparer.Instance);
        foreach (var newItem in newItems)
        {
            if (newItem.SizeBytes is null
                && currentItemMap.TryGetValue(PathKeyOf(newItem), out var existingItem)
                && existingItem.SizeBytes is { } existingSize)
            {
                newItem.SizeBytes = existingSize;
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

        // Location は ParentPath と同じ値になるため、GetDirectoryName で行ごとに文字列を
        // 再生成せず、リポジトリ側でプール済みのインスタンスを共有する
        var item = entry.IsFolder
            ? new FileItemViewModel(entry.FullPath, entry.Name, modifiedLocalTime, location: entry.ParentPath)
            : new FileItemViewModel(entry.FullPath, entry.Name, entry.SizeBytes ?? 0L, modifiedLocalTime, location: entry.ParentPath);

        // 作成日時・属性は列追加前のキャッシュ行には無い（null）ため、次のスキャンで埋まるまで空表示になる
        if (entry.CreationTimeUtc is { } creationUtc)
        {
            item.CreatedAt = DateTime.SpecifyKind(creationUtc, DateTimeKind.Utc).ToLocalTime();
        }

        item.AttributesText = FormatAttributes(entry.Attributes);
        return item;
    }

    // 表記は16通りしかないため事前生成して共有し、1アイテムごとの文字列生成・保持をなくす
    private static readonly string[] AttributeTextByMask = BuildAttributeTextByMask();

    private static string[] BuildAttributeTextByMask()
    {
        var texts = new string[16];
        Span<char> letters = stackalloc char[4];

        for (var mask = 0; mask < texts.Length; mask++)
        {
            var count = 0;
            if ((mask & 1) != 0) letters[count++] = 'R';
            if ((mask & 2) != 0) letters[count++] = 'H';
            if ((mask & 4) != 0) letters[count++] = 'S';
            if ((mask & 8) != 0) letters[count++] = 'A';
            texts[mask] = count == 0 ? string.Empty : new string(letters[..count]);
        }

        return texts;
    }

    /// <summary>ファイル属性をエクスプローラー風の文字列（R/H/S/A）へ変換する。未取得（null）や該当なしは空。</summary>
    private static string FormatAttributes(int? attributes)
    {
        if (attributes is null)
        {
            return string.Empty;
        }

        var value = (FileAttributes)attributes.Value;
        var mask = (value.HasFlag(FileAttributes.ReadOnly) ? 1 : 0)
                 | (value.HasFlag(FileAttributes.Hidden) ? 2 : 0)
                 | (value.HasFlag(FileAttributes.System) ? 4 : 0)
                 | (value.HasFlag(FileAttributes.Archive) ? 8 : 0);

        return AttributeTextByMask[mask];
    }
}
