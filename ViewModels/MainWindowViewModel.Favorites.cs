using System.Collections.ObjectModel;
using ParallelScope.Data;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// ツリー最上位の「★ Favorites」「🕒 Frequently Used」ノードに関する処理。
/// どちらもPlus機能のため、未購読の間はツリーに出さない（SetPlusFeaturesEnabled）。
/// </summary>
public partial class MainWindowViewModel
{
    /// <summary>「Frequently Used」に並べるフォルダの最大件数。</summary>
    private const int MaxFrequentFolders = 10;

    // お気に入りの登録順（正規化済みパス）。ツリーの並び順もこの順になる
    private List<string> _favoritePaths = new();

    // フォルダごとのアクセス実績（キーは正規化済みパス）
    private Dictionary<string, FolderUsageEntry> _folderUsages = new(StringComparer.OrdinalIgnoreCase);

    private bool _arePlusFeaturesEnabled;

    /// <summary>「★ Favorites」ノードの子（お気に入りフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _favoriteFolders = new();

    /// <summary>「🕒 Frequently Used」ノードの子（アクセス回数の多いフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _frequentFolders = new();

    /// <summary>ツリー最上位の「Folders」ノード。ツリー選択の同期で起点として使う。</summary>
    public FolderItemViewModel AllRootsNode { get; private set; } = null!;

    private FolderItemViewModel _favoritesNode = null!;
    private FolderItemViewModel _frequentNode = null!;

    /// <summary>ツリー最上位のノードを生成する（表示するかどうかは SetPlusFeaturesEnabled が決める）。</summary>
    private void InitializeTreeNodes()
    {
        AllRootsNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.AllRoots, _rootFolders, isExpanded: true);
        // お気に入り・よく使うは件数が少なく一覧性が高いので、最初から展開しておく
        _favoritesNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.Favorites, _favoriteFolders, isExpanded: true);
        _frequentNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.Frequent, _frequentFolders, isExpanded: true);

        TreeRoots.Add(AllRootsNode);
    }

    /// <summary>
    /// Plus機能（お気に入り・よく使う）の有効/無効を切り替える。
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
            // 「Folders」より上に、Favorites → Frequently Used の順で差し込む
            TreeRoots.Insert(0, _favoritesNode);
            TreeRoots.Insert(1, _frequentNode);
            RefreshFrequentFolders();
            return;
        }

        TreeRoots.Remove(_favoritesNode);
        TreeRoots.Remove(_frequentNode);

        // 非表示中に仮想ノードを表示したままにならないよう、「Folders」へ退避する
        if (VirtualFolders.GetKind(CurrentPath) is VirtualFolderKind.Favorites or VirtualFolderKind.Frequent)
        {
            NavigateTo(VirtualFolders.AllRootsPath, false);
        }
    }

    /// <summary>Plus機能（お気に入り・よく使う）が現在有効か。</summary>
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

        // お気に入りは「よく使う」から除外しているため、登録/解除のたびに並べ直す
        RefreshFrequentFolders();

        if (VirtualFolders.GetKind(CurrentPath) == VirtualFolderKind.Favorites)
        {
            RefreshCurrentFolder();
        }

        return index < 0;
    }

    /// <summary>フォルダへ移動したことを記録する（「よく使う」の並び順の元データ）。</summary>
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

    /// <summary>仮想ノードに対応するフォルダのパス一覧を返す（実パスの場合はそのパス自身）。</summary>
    private IReadOnlyList<string> GetVirtualFolderPaths(VirtualFolderKind kind) => kind switch
    {
        VirtualFolderKind.AllRoots => _rootPathsSnapshot,
        VirtualFolderKind.Favorites => GetFavoritePaths(),
        VirtualFolderKind.Frequent => GetFrequentPaths(),
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

        RefreshFrequentFolders();
    }

    /// <summary>お気に入り／よく使う配下に置く、実体ツリーの複製ノードを生成する。</summary>
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
