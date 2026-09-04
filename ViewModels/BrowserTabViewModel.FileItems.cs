using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>FileItems（画面表示用コレクション）の差分更新とキャッシュエントリ→ViewModel変換。</summary>
public partial class BrowserTabViewModel
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
        // 呼び出し元はいずれも List を渡し、渡した後に中身を書き換えないため複製しない
        // （検索・All Filesでは数十万件のコピーがそのままUIスレッドの停止時間になる）
        var newItems = items as List<FileItemViewModel> ?? items.ToList();
        var replacedItemCount = Math.Max(FileItems.Count, newItems.Count);

        // 件数が閾値を超えて増減していれば、差分を数えるまでもなく一括差し替えに決まる
        // （重なるのは多くても少ない方の件数ぶんなので、追加と削除の合計は必ず件数差以上になる）。
        // この近道が無いと、結論が決まっているのに全件ぶんのパスのハッシュ計算が
        // UIスレッドで走ってしまう —— 97万件ヒットする検索や、そこから数件へ絞り込む場面がこれに当たる。
        // 既存インスタンスの使い回しはここで諦めるが、フォルダの合計サイズは
        // _currentDirectoryItems 側で引き継がれ、直後のキャッシュ集計でも入れ直される
        var isBulkReplaceCertain = Math.Abs(newItems.Count - FileItems.Count) > BulkReplaceThreshold;

        if (forceBulkReplace || isBulkReplaceCertain)
        {
            // モード切り替え時の大量データでは差分計算自体が高コストになるため、
            // 一括差し替えでUIスレッドのブロック時間を短縮する。
            FileItems = new ObservableCollection<FileItemViewModel>(newItems);
            ScheduleMemoryTrim(replacedItemCount);
            return;
        }

        // 既存アイテムをパスでマップし、新しい一覧と突き合わせが済んだものは取り除いていく。
        // 最後まで残ったものがそのまま「削除するアイテム」になる。
        // （新旧のキー集合を別々に作って突き合わせると、パス文字列のハッシュ計算が
        //   件数×数回ぶんUIスレッドで走る。1パスに畳んで15万件で約1/3の時間にしている）
        var remainingItems = new Dictionary<(string Location, string Name), FileItemViewModel>(
            FileItems.Count, PathKeyComparer.Instance);
        foreach (var item in FileItems)
        {
            // 大文字小文字だけ異なる同じパスがキャッシュに残っているとキーが衝突するため、
            // Add ではなくインデクサーで入れる（衝突した側は突き合わせ対象から外れ、削除される）
            remainingItems[PathKeyOf(item)] = item;
        }

        // 並びは新しい一覧のとおり。既存アイテムはサイズ表示等を保持するためインスタンスを再利用する
        var mergedItems = new List<FileItemViewModel>(newItems.Count);
        var itemsToAdd = new List<FileItemViewModel>();
        var addedItemCount = 0;

        foreach (var newItem in newItems)
        {
            if (remainingItems.Remove(PathKeyOf(newItem), out var existingItem))
            {
                ApplyItemUpdate(existingItem, newItem);
                mergedItems.Add(existingItem);
                continue;
            }

            mergedItems.Add(newItem);
            addedItemCount++;

            // 追加が閾値を超えた時点で一括差し替えが確定するため、それ以降は控えない
            if (addedItemCount <= BulkReplaceThreshold)
            {
                itemsToAdd.Add(newItem);
            }
        }

        if (remainingItems.Count + addedItemCount > BulkReplaceThreshold)
        {
            // 1件ずつのAdd/RemoveはCollectionChanged通知が件数分発生し（Removeは1件ごとに線形探索も走る）、
            // 大量差分ではUIスレッドが数秒単位でブロックされる。コレクション差し替えなら再バインド1回で済み、
            // DataGridの行仮想化により生成されるのは可視行のみ。
            FileItems = new ObservableCollection<FileItemViewModel>(mergedItems);
            ScheduleMemoryTrim(replacedItemCount);
            return;
        }

        // 削除
        foreach (var item in remainingItems.Values)
        {
            FileItems.Remove(item);
        }

        // 追加
        foreach (var item in itemsToAdd)
        {
            FileItems.Add(item);
        }
    }

    /// <summary>この件数を超える一覧の入れ替えの後は、メモリ返却のためのGCを予約する。</summary>
    private const int MemoryTrimItemCountThreshold = 50_000;
    private int _memoryTrimVersion;

    /// <summary>
    /// All Filesモード等での大量アイテム入れ替え後、不要になった旧一覧のメモリをOSへ返す。
    /// 数十万件規模の入れ替えではGen2/LOHに旧一覧が残り、自然なGCまで（さらにGC後も
    /// コミット済みのまま）ワーキングセットが積み上がるため、明示的に回収・返却する。
    /// 連続ナビゲーション中に毎回ブロッキングGCが走らないよう、一定時間の静止後に最新の1回だけ実行する。
    /// </summary>
    private void ScheduleMemoryTrim(int replacedItemCount)
    {
        if (replacedItemCount < MemoryTrimItemCountThreshold)
        {
            return;
        }

        var version = Interlocked.Increment(ref _memoryTrimVersion);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (version != Volatile.Read(ref _memoryTrimVersion))
            {
                return;
            }

            // プールされたSQLite接続が抱えるページキャッシュ（接続あたり最大16MB）のネイティブメモリも返却する
            // （使用中の接続には影響せず、次回アクセス時の再接続はローカルファイルでは数ms程度）
            _host.FileCacheRepository.ReleasePooledConnections();

            // Aggressive はLOHを含む全ヒープを圧縮し、空き領域をOSへ返却する
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
        });
    }

    /// <summary>既存アイテムのプロパティを新規アイテムの情報で更新する。</summary>
    private static void ApplyItemUpdate(FileItemViewModel existing, FileItemViewModel newItem)
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

    /// <summary>フォルダ行だけをパスで引けるようにマップ化する。</summary>
    private static Dictionary<(string Location, string Name), FileItemViewModel> BuildFolderLookup(
        IEnumerable<FileItemViewModel> items)
    {
        var result = new Dictionary<(string Location, string Name), FileItemViewModel>(PathKeyComparer.Instance);

        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                result[PathKeyOf(item)] = item;
            }
        }

        return result;
    }

    /// <summary>現在フォルダの全件（検索対象外の基準データ）を更新し、検索中でなければ表示にも反映する。</summary>
    private void UpdateCurrentDirectoryItems(IEnumerable<FileItemViewModel> items)
    {
        // 呼び出し元はいずれもバックグラウンドで組み立てた List を渡し、渡した後に中身を書き換えないため複製しない
        var newItems = items as List<FileItemViewModel> ?? items.ToList();

        // 既存のアイテムからフォルダ合計サイズを引き継ぐ（キャッシュ集計でしか得られない値のため、
        // ライブデータの再取得時に失われるのを防止）。ファイルはライブ列挙の最新サイズをそのまま使う。
        // 引き継ぎ対象になるのはサイズ未取得のフォルダ行だけなので、突き合わせ表もフォルダ行だけで作る
        // （数万ファイルのフォルダでも、この表はフォルダ数ぶんで済む）
        Dictionary<(string Location, string Name), FileItemViewModel>? currentFolderMap = null;
        foreach (var newItem in newItems)
        {
            if (newItem.SizeBytes is not null || !newItem.IsFolder)
            {
                continue;
            }

            currentFolderMap ??= BuildFolderLookup(_currentDirectoryItems);
            if (currentFolderMap.TryGetValue(PathKeyOf(newItem), out var existingItem)
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

    /// <summary>
    /// キャッシュエントリを、隠し/システム属性の表示設定で絞り込みつつ画面表示用ViewModelへ変換する。
    /// キャッシュには属性に関わらず全件入っているため、絞り込みはここ（表示側）だけで行う。
    /// </summary>
    private IEnumerable<FileItemViewModel> ToViewModels(IEnumerable<CachedFileSystemEntry> entries)
    {
        var showHidden = _host.ShowHiddenItems;
        var showSystem = _host.ShowSystemItems;

        return entries
            .Where(entry => HiddenItemVisibility.IsVisible(entry.Attributes, showHidden, showSystem))
            .Select(ToViewModel);
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
