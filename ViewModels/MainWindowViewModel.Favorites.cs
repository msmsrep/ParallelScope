using System.Collections.ObjectModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// ツリー最上位の「★ Favorites」「🕘 Recent」「🕒 Frequently Used」ノードに関する処理。
/// いずれもPlus機能のため、未購読の間はツリーに出さない（SetPlusFeaturesEnabled）。
/// Recent と Frequently Used はどちらも _folderUsages（フォルダごとのアクセス実績）が元データで、
/// 並べる基準だけが違う（Recent は最終アクセスが新しい順、Frequently Used はアクセス回数の多い順）。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>「Frequently Used」に並べるフォルダの最大件数。</summary>
    private const int MaxFrequentFolders = 10;

    /// <summary>「Recent」に並べるフォルダの最大件数（履歴なので「よく使う」より多めに出す）。</summary>
    private const int MaxRecentFolders = 20;

    // お気に入りの登録順（正規化済みパス）。ツリーの並び順もこの順になる
    private List<string> _favoritePaths = new();

    // フォルダごとのアクセス実績（キーは正規化済みパス）
    private Dictionary<string, FolderUsageEntry> _folderUsages = new(StringComparer.OrdinalIgnoreCase);

    private bool _arePlusFeaturesEnabled;

    // ツリー最上位のノードの表示設定（キーは TreeNodes 参照）。「Folders」は常に表示のため _visibleTreeNodes には入らない
    private HashSet<string> _visibleTreeNodes = TreeNodes.DefaultVisibleNodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private List<string> _treeNodeOrder = TreeNodes.AllNodes.ToList();

    /// <summary>「★ Favorites」ノードの子（お気に入りフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _favoriteFolders = new();

    /// <summary>「🕒 Frequently Used」ノードの子（アクセス回数の多いフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _frequentFolders = new();

    /// <summary>「🕘 Recent」ノードの子（最近開いたフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _recentFolders = new();

    /// <summary>ツリー最上位の「Folders」ノード。ツリー選択の同期で起点として使う。</summary>
    public FolderItemViewModel AllRootsNode { get; private set; } = null!;

    private FolderItemViewModel _favoritesNode = null!;
    private FolderItemViewModel _frequentNode = null!;
    private FolderItemViewModel _recentNode = null!;

    /// <summary>ツリー最上位のノードを生成する（表示するかどうかは SetPlusFeaturesEnabled が決める）。</summary>
    private void InitializeTreeNodes()
    {
        AllRootsNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.AllRoots, _rootFolders, isExpanded: true);
        // お気に入り・最近・よく使うは、ルートフォルダの一覧（Folders）を見渡しやすくするため既定では閉じておく
        _favoritesNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.Favorites, _favoriteFolders, isExpanded: false);
        _frequentNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.Frequent, _frequentFolders, isExpanded: false);
        _recentNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.Recent, _recentFolders, isExpanded: false);

        RebuildTreeRoots();
    }

    /// <summary>ノードキーに対応する最上位ノードを返す（未知のキーはnull）。</summary>
    private FolderItemViewModel? GetTreeNode(string key) => TreeNodes.GetKind(key) switch
    {
        VirtualFolderKind.AllRoots => AllRootsNode,
        VirtualFolderKind.Favorites => _favoritesNode,
        VirtualFolderKind.Frequent => _frequentNode,
        VirtualFolderKind.Recent => _recentNode,
        _ => null
    };

    /// <summary>
    /// 設定（並び順・表示するノード・Plusの購読状態）どおりに TreeRoots を組み立て直す。
    /// ノードのインスタンスは使い回し、位置がずれている分だけ差分で入れ替える
    /// （毎回作り直すとTreeViewItemが再生成され、展開状態や選択が飛んでしまうため）。
    /// </summary>
    private void RebuildTreeRoots()
    {
        var visibleOptionalNodes = TreeNodes
            .GetEffectiveVisibleNodes(_visibleTreeNodes, _arePlusFeaturesEnabled)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var desired = _treeNodeOrder
            // 「Folders」は非表示にできない（全て隠すとツリーが空になってしまうため）
            .Where(key => string.Equals(key, TreeNodes.AllRoots, StringComparison.OrdinalIgnoreCase)
                || visibleOptionalNodes.Contains(key))
            .Select(GetTreeNode)
            .OfType<FolderItemViewModel>()
            .ToList();

        for (var index = 0; index < desired.Count; index++)
        {
            var currentIndex = TreeRoots.IndexOf(desired[index]);
            if (currentIndex < 0)
            {
                TreeRoots.Insert(index, desired[index]);
            }
            else if (currentIndex != index)
            {
                // index より前は確定済みで、ノードは重複しないため currentIndex は必ず index より後ろ
                TreeRoots.Move(currentIndex, index);
            }
        }

        // 目的の並びに含まれなかったノードは末尾へ押し出されているので、まとめて取り除く
        while (TreeRoots.Count > desired.Count)
        {
            TreeRoots.RemoveAt(TreeRoots.Count - 1);
        }

        LeaveHiddenVirtualFolder();
    }

    /// <summary>ツリーから消えた仮想ノードを開いたままにしないよう、「Folders」へ退避する。</summary>
    private void LeaveHiddenVirtualFolder()
    {
        if (!VirtualFolders.IsVirtual(CurrentPath))
        {
            return;
        }

        if (TreeRoots.Any(node => string.Equals(node.Path, CurrentPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        NavigateTo(VirtualFolders.AllRootsPath, false);
    }

    /// <summary>ツリーに表示するノードのキー一覧（並び順どおり。常に表示の「Folders」は含まない）。</summary>
    public IReadOnlyList<string> GetVisibleTreeNodes()
    {
        return _treeNodeOrder.Where(_visibleTreeNodes.Contains).ToList();
    }

    /// <summary>ツリー最上位のノードの並び順（「Folders」を含む全てのキー）。</summary>
    public IReadOnlyList<string> GetTreeNodeOrder()
    {
        return _treeNodeOrder.ToList();
    }

    /// <summary>
    /// 言語切り替え後に、ツリー上の言語依存の表示名（仮想ノード・遅延読み込み中のダミー）を引き直す。
    /// TreeRootsに出ていない（未購読時の）お気に入り・最近・よく使うノードも含めて更新する。
    /// </summary>
    private void RefreshLocalizedTreeNames()
    {
        AllRootsNode.RefreshLocalizedDisplayName();
        _favoritesNode.RefreshLocalizedDisplayName();
        _frequentNode.RefreshLocalizedDisplayName();
        _recentNode.RefreshLocalizedDisplayName();
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
            RefreshRecentFolders();
            RefreshFrequentFolders();
        }

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
            // 子ノードは登録順と同じ並びなので同じ位置を消せばよい（丸ごと作り直すと他ノードの展開状態が失われる）
            _favoriteFolders.RemoveAt(index);
        }
        else
        {
            _favoritePaths.Add(normalized);
            _favoriteFolders.Add(CreateShortcutNode(normalized));
        }

        SaveSettings(RootFolders.Select(x => x.Path));

        // お気に入りは「最近」「よく使う」から除外しているため、登録/解除のたびに並べ直す
        RefreshRecentFolders();
        RefreshFrequentFolders();

        if (VirtualFolders.GetKind(CurrentPath) == VirtualFolderKind.Favorites)
        {
            RefreshCurrentFolder();
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
        }

        try
        {
            SaveSettings(RootFolders.Select(x => x.Path));
        }
        catch
        {
            // 移動のたびに保存するため、設定ファイルが一時的に書けない状況でも
            // ナビゲーション自体は失敗させない（記録はメモリ上に残り、次回の保存で書き出される）
        }
    }

    /// <summary>
    /// 「よく使う」の一覧を最新のアクセス実績で並べ直す。
    /// 移動のたびに並べ替えるとツリーが目の前で動いてしまうため、起動時とノードの展開時にだけ呼ぶ。
    /// </summary>
    public void RefreshFrequentFolders()
    {
        var paths = GetFrequentPaths();

        // 並びが変わっていなければ作り直さない（展開状態を保つ）
        if (paths.Count == _frequentFolders.Count
            && paths.Zip(_frequentFolders).All(pair => string.Equals(pair.First, pair.Second.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _frequentFolders.Clear();
        foreach (var path in paths)
        {
            _frequentFolders.Add(CreateShortcutNode(path));
        }
    }

    /// <summary>
    /// 「最近」の一覧を最新のアクセス実績で並べ直す。
    /// 「よく使う」と同じく、移動のたびに並べ替えるとツリーが目の前で動いてしまうため、
    /// 起動時とノードの展開時にだけ呼ぶ（ノードを開いた先のファイル一覧は常に最新の順で作られる）。
    /// </summary>
    public void RefreshRecentFolders()
    {
        var paths = GetRecentPaths();

        // 並びが変わっていなければ作り直さない（展開状態を保つ）
        if (paths.Count == _recentFolders.Count
            && paths.Zip(_recentFolders).All(pair => string.Equals(pair.First, pair.Second.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _recentFolders.Clear();
        foreach (var path in paths)
        {
            _recentFolders.Add(CreateShortcutNode(path));
        }
    }

    /// <summary>お気に入りフォルダのパス一覧（登録順）。ユーザーが明示的に登録したものなので除外設定では絞らない。</summary>
    public IReadOnlyList<string> GetFavoritePaths()
    {
        return _favoritePaths.ToList();
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

    /// <summary>保存済みのお気に入り・アクセス実績を読み込み、ツリーの子ノードを構築する。</summary>
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

        _favoriteFolders.Clear();
        foreach (var path in _favoritePaths)
        {
            _favoriteFolders.Add(CreateShortcutNode(path));
        }

        RefreshRecentFolders();
        RefreshFrequentFolders();
    }

    /// <summary>お気に入り／最近／よく使う配下に置く、実体ツリーの複製ノードを生成する。</summary>
    private FolderItemViewModel CreateShortcutNode(string path)
    {
        return new FolderItemViewModel(path, IsExcludedPath, isShortcut: true);
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
