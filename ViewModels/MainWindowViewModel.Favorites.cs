using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// ツリー最上位の「★ Favorites」「🕘 Recent」「🕒 Frequently Used」の元データに関する処理。
/// いずれもPlus機能のため、未購読の間はツリーに出さない（SetPlusFeaturesEnabled）。
/// Recent と Frequently Used はどちらも _folderUsages（フォルダごとのアクセス実績）が元データで、
/// 並べる基準だけが違う（Recent は最終アクセスが新しい順、Frequently Used はアクセス回数の多い順）。
/// ノードのViewModelはツリーごと（＝ペインごと）に作られるため、ここは値の提供と変更の通知だけを行う。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>「Frequently Used」に並べるフォルダの最大件数。</summary>
    private const int MaxFrequentFolders = 10;

    /// <summary>「Recent」に並べるフォルダの最大件数（履歴なので「よく使う」より多めに出す）。</summary>
    private const int MaxRecentFolders = 20;

    /// <summary>
    /// アクセス実績を控えておくフォルダの上限。表示に使うのは「最近」20件と「よく使う」10件だけだが、
    /// 回数を積み上げる余地を持たせるためもっと多めに残す。
    /// 上限が無いと訪問したフォルダの数だけ settings.json が際限なく育ち、
    /// 移動のたびの書き出し（=全件のJSON生成）が少しずつ重くなっていく。
    /// </summary>
    internal const int MaxFolderUsages = 200;

    // お気に入りの登録順（正規化済みパス）。ツリーの並び順もこの順になる
    private List<string> _favoritePaths = new();

    // フォルダごとのアクセス実績（キーは正規化済みパス）
    private Dictionary<string, FolderUsageEntry> _folderUsages = new(StringComparer.OrdinalIgnoreCase);

    private bool _arePlusFeaturesEnabled;

    // ツリー最上位のノードの表示設定（キーは TreeNodes 参照）。「Folders」は常に表示のため _visibleTreeNodes には入らない
    private HashSet<string> _visibleTreeNodes = TreeNodes.DefaultVisibleNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private List<string> _treeNodeOrder = TreeNodes.AllNodes.ToList();

    /// <summary>ツリーに表示するノードのキー一覧（並び順どおり。常に表示の「Folders」は含まない）。</summary>
    public IReadOnlyList<string> GetVisibleTreeNodes()
    {
        return _treeNodeOrder.Where(_visibleTreeNodes.Contains).ToList();
    }

    /// <summary>購読状態も踏まえて、実際にツリーへ出すオプションノードのキー一覧を返す。</summary>
    internal IReadOnlyList<string> GetEffectiveVisibleTreeNodes()
    {
        return TreeNodes.GetEffectiveVisibleNodes(_visibleTreeNodes, _arePlusFeaturesEnabled);
    }

    /// <summary>ツリー最上位のノードの並び順（「Folders」を含む全てのキー）。</summary>
    public IReadOnlyList<string> GetTreeNodeOrder()
    {
        return _treeNodeOrder.ToList();
    }

    /// <summary>全ペインのツリーを、現在の表示設定・購読状態どおりに組み立て直す。</summary>
    private void RebuildTreeRoots()
    {
        foreach (var pane in Panes)
        {
            pane.RebuildTreeRoots();
        }

        LeaveHiddenVirtualFolder();
    }

    /// <summary>ツリーから消えた仮想ノードを開いたままにしないよう、「Folders」へ退避する。</summary>
    private void LeaveHiddenVirtualFolder()
    {
        var visibleNodes = GetEffectiveVisibleTreeNodes().ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 表示していないタブも同じノードを開いたままにできないため、全タブを対象にする
        foreach (var tab in AllTabs)
        {
            var kind = VirtualFolders.GetKind(tab.CurrentPath);
            if (kind is VirtualFolderKind.None or VirtualFolderKind.AllRoots)
            {
                continue;
            }

            if (visibleNodes.Contains(TreeNodes.GetKey(kind)))
            {
                continue;
            }

            tab.NavigateTo(VirtualFolders.AllRootsPath, false);
        }
    }

    /// <summary>言語切り替え後に、ツリー上の言語依存の表示名（仮想ノード・遅延読み込み中のダミー）を引き直す。</summary>
    private void RefreshLocalizedTreeNames()
    {
        foreach (var pane in Panes)
        {
            pane.RefreshLocalizedTreeNames();
        }
    }

    /// <summary>
    /// Plus機能（お気に入り・最近・よく使う）の有効/無効を切り替える。
    /// 起動直後はライセンス未取得のため無効で、購読が確認できた時点で呼び直される。
    /// </summary>
    public void SetPlusFeaturesEnabled(bool isEnabled)
    {
        if (_arePlusFeaturesEnabled == isEnabled)
        {
            return;
        }

        _arePlusFeaturesEnabled = isEnabled;

        if (isEnabled)
        {
            RefreshUsageFolders();
        }

        // ファイル名索引もPlus機能。購読が切れたらここで捨ててメモリを返す
        ApplyNameIndexState();

        // 正規表現検索もPlus機能。切り替わると同じ検索語でも結果が変わるため、検索し直させる
        NotifySearchModeChanged();
        RebuildTreeRoots();
    }

    /// <summary>Plus機能（お気に入り・最近・よく使う）が現在有効か。</summary>
    public bool ArePlusFeaturesEnabled => _arePlusFeaturesEnabled;

    /// <summary>指定フォルダがお気に入りに登録済みか。</summary>
    public bool IsFavorite(string path)
    {
        var normalized = TryNormalizeRealPath(path);
        return normalized is not null
            && _favoritePaths.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>お気に入りの登録/解除を切り替える。戻り値は切り替え後に登録済みかどうか。</summary>
    public bool ToggleFavorite(string path)
    {
        var normalized = TryNormalizeRealPath(path);
        if (normalized is null)
        {
            return false;
        }

        var index = _favoritePaths.FindIndex(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _favoritePaths.RemoveAt(index);
        }
        else
        {
            _favoritePaths.Add(normalized);
        }

        SaveSettings();

        foreach (var pane in Panes)
        {
            pane.RefreshFavoriteFolders();
        }

        // お気に入りは「最近」「よく使う」から除外しているため、登録/解除のたびに並べ直す
        RefreshUsageFolders();

        foreach (var tab in AllTabs.Where(tab => VirtualFolders.GetKind(tab.CurrentPath) == VirtualFolderKind.Favorites))
        {
            tab.RefreshCurrentFolder();
        }

        return index < 0;
    }

    /// <summary>フォルダへ移動したことを記録する（「最近」「よく使う」の並び順の元データ）。</summary>
    private void RecordFolderUsage(string path)
    {
        var normalized = TryNormalizeRealPath(path);
        if (normalized is null)
        {
            return;
        }

        if (_folderUsages.TryGetValue(normalized, out var usage))
        {
            usage.Count++;
            usage.LastAccessedAt = DateTime.Now;
        }
        else
        {
            _folderUsages[normalized] = new FolderUsageEntry
            {
                Path = normalized,
                Count = 1,
                LastAccessedAt = DateTime.Now
            };

            // 増えるのは新しいフォルダを開いたときだけなので、絞り込みもここでだけ試みる
            TrimFolderUsages();
        }

        // 書き出しはリポジトリ側で遅延・集約され、失敗しても黙って諦めるため、
        // 移動のたびに呼んでもナビゲーションを止めることはない
        SaveSettings();
    }

    /// <summary>全ペインの「最近」「よく使う」を最新のアクセス実績で並べ直す。</summary>
    private void RefreshUsageFolders()
    {
        foreach (var pane in Panes)
        {
            pane.RefreshRecentFolders();
            pane.RefreshFrequentFolders();
        }
    }

    /// <summary>お気に入りフォルダのパス一覧（登録順）。ユーザーが明示的に登録したものなので除外設定では絞らない。</summary>
    public IReadOnlyList<string> GetFavoritePaths()
    {
        return _favoritePaths.ToList();
    }

    /// <summary>
    /// アクセス実績を上限（<see cref="MaxFolderUsages"/>）まで絞る。最終アクセスが古いものから捨てるが、
    /// 「よく使う」に並ぶ回数上位だけは最近触っていなくても残す
    /// —— 捨てると回数が0から数え直しになり、よく使うフォルダが一覧から消えてしまうため。
    /// </summary>
    private void TrimFolderUsages()
    {
        if (_folderUsages.Count <= MaxFolderUsages)
        {
            return;
        }

        var frequentlyUsed = _folderUsages.Values
            .OrderByDescending(usage => usage.Count)
            .ThenByDescending(usage => usage.LastAccessedAt)
            .Take(MaxFrequentFolders)
            .ToHashSet();

        _folderUsages = _folderUsages.Values
            .OrderByDescending(frequentlyUsed.Contains)
            .ThenByDescending(usage => usage.LastAccessedAt)
            .Take(MaxFolderUsages)
            .ToDictionary(usage => usage.Path, usage => usage, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>アクセス回数の多いフォルダのパス一覧（回数の多い順、同数なら最終アクセスが新しい順）。</summary>
    public IReadOnlyList<string> GetFrequentPaths()
    {
        return _folderUsages.Values
            // お気に入りは専用ノードに並ぶため、ここでは重ねて出さない
            .Where(usage => !_favoritePaths.Contains(usage.Path, StringComparer.OrdinalIgnoreCase)
                && !IsExcludedPath(usage.Path))
            .OrderByDescending(usage => usage.Count)
            .ThenByDescending(usage => usage.LastAccessedAt)
            .Take(MaxFrequentFolders)
            .Select(usage => usage.Path)
            .ToList();
    }

    /// <summary>最近開いたフォルダのパス一覧（最終アクセスが新しい順）。</summary>
    public IReadOnlyList<string> GetRecentPaths()
    {
        return _folderUsages.Values
            // お気に入りは専用ノードに並ぶため、「よく使う」と同じくここでは重ねて出さない
            .Where(usage => !_favoritePaths.Contains(usage.Path, StringComparer.OrdinalIgnoreCase)
                && !IsExcludedPath(usage.Path))
            .OrderByDescending(usage => usage.LastAccessedAt)
            .Take(MaxRecentFolders)
            .Select(usage => usage.Path)
            .ToList();
    }

    /// <summary>仮想ノードに対応するフォルダのパス一覧を返す（実パスの場合はそのパス自身）。</summary>
    private IReadOnlyList<string> GetVirtualFolderPaths(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.AllRoots => _rootPathsSnapshot,
        VirtualFolderKind.Favorites => GetFavoritePaths(),
        VirtualFolderKind.Frequent => GetFrequentPaths(),
        VirtualFolderKind.Recent => GetRecentPaths(),
        _ => Array.Empty<string>()
    };

    /// <summary>保存済みのお気に入り・アクセス実績を読み込み、各ペインのツリーへ反映する。</summary>
    private void LoadFavoritesAndUsage(AppSettings settings)
    {
        _favoritePaths = NormalizeStoredPaths(settings.FavoritePaths).ToList();

        _folderUsages = new Dictionary<string, FolderUsageEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var usage in settings.FolderUsages ?? Enumerable.Empty<FolderUsageEntry>())
        {
            var normalized = TryNormalizeRealPath(usage.Path);
            if (normalized is null || usage.Count <= 0)
            {
                continue;
            }

            // 正規化の結果が重複した場合は回数を合算する
            if (_folderUsages.TryGetValue(normalized, out var existing))
            {
                existing.Count += usage.Count;
                existing.LastAccessedAt = existing.LastAccessedAt > usage.LastAccessedAt ? existing.LastAccessedAt : usage.LastAccessedAt;
                continue;
            }

            _folderUsages[normalized] = new FolderUsageEntry
            {
                Path = normalized,
                Count = usage.Count,
                LastAccessedAt = usage.LastAccessedAt
            };
        }

        // 上限を設ける前の settings.json には際限なく貯まっているため、読み込み時にも絞る
        TrimFolderUsages();

        foreach (var pane in Panes)
        {
            pane.RefreshFavoriteFolders();
        }

        RefreshUsageFolders();
    }

    /// <summary>実在パスとして正規化する。仮想パス・空・不正な形式はnullを返す。</summary>
    private static string? TryNormalizeRealPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || VirtualFolders.IsVirtual(path))
        {
            return null;
        }

        try
        {
            var normalized = PathNormalizer.Normalize(path);
            return string.IsNullOrEmpty(normalized) ? null : normalized;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>設定ファイル由来のパス一覧を正規化し、重複と不正な値を落とす（存在確認はしない）。</summary>
    private static IEnumerable<string> NormalizeStoredPaths(IEnumerable<string>? paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths ?? Enumerable.Empty<string>())
        {
            // 切断中のNAS配下のお気に入りが次の保存で失われないよう、ここでは存在確認をしない
            var normalized = TryNormalizeRealPath(path);
            if (normalized is not null && seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }
}
