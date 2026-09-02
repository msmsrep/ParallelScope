using System.Collections.ObjectModel;
using ParallelScope.Utilities;

namespace ParallelScope.ViewModels;

/// <summary>
/// ペインが持つフォルダツリー。ツリーはペインごとに独立している（展開状態・選択位置が別）ため、
/// ノードのViewModelもペインごとに作る。元データ（ルートパス・お気に入り・アクセス実績・
/// 表示するノードの設定）はアプリ全体で1つなのでシェルが持ち、変化のたびにシェルから呼び直される。
/// </summary>
public partial class BrowserPaneViewModel
{
    /// <summary>「★ Favorites」ノードの子（お気に入りフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _favoriteFolders = new();

    /// <summary>「🕒 Frequently Used」ノードの子（アクセス回数の多いフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _frequentFolders = new();

    /// <summary>「🕘 Recent」ノードの子（最近開いたフォルダ）。</summary>
    private readonly ObservableCollection<FolderItemViewModel> _recentFolders = new();

    private FolderItemViewModel _favoritesNode = null!;
    private FolderItemViewModel _frequentNode = null!;
    private FolderItemViewModel _recentNode = null!;

    /// <summary>ルートフォルダのノード（仮想「Folders」ノードの子）。</summary>
    public ObservableCollection<FolderItemViewModel> RootFolders { get; } = new();

    /// <summary>
    /// フォルダツリーに表示する最上位ノード。全ルートを子に持つ仮想「Folders」ノードは常に含まれ、
    /// ルートの増減は共有している RootFolders コレクション経由で自動的に反映される。
    /// </summary>
    public ObservableCollection<FolderItemViewModel> TreeRoots { get; } = new();

    /// <summary>ツリー最上位の「Folders」ノード。ツリー選択の同期で起点として使う。</summary>
    public FolderItemViewModel AllRootsNode { get; private set; } = null!;

    /// <summary>ツリー最上位のノードを生成する（表示するかどうかは購読状態と設定が決める）。</summary>
    private void InitializeTreeNodes()
    {
        AllRootsNode = FolderItemViewModel.CreateVirtualNode(VirtualFolderKind.AllRoots, RootFolders, isExpanded: true);
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
    internal void RebuildTreeRoots()
    {
        var visibleOptionalNodes = _shell.GetEffectiveVisibleTreeNodes()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var desired = _shell.GetTreeNodeOrder()
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
    }

    /// <summary>お気に入りの一覧をシェルの登録内容に合わせて作り直す。</summary>
    internal void RefreshFavoriteFolders()
    {
        ReplaceShortcutNodes(_favoriteFolders, _shell.GetFavoritePaths());
    }

    /// <summary>
    /// 「よく使う」の一覧を最新のアクセス実績で並べ直す。
    /// 移動のたびに並べ替えるとツリーが目の前で動いてしまうため、起動時とノードの展開時にだけ呼ぶ。
    /// </summary>
    public void RefreshFrequentFolders()
    {
        ReplaceShortcutNodes(_frequentFolders, _shell.GetFrequentPaths());
    }

    /// <summary>
    /// 「最近」の一覧を最新のアクセス実績で並べ直す。
    /// 「よく使う」と同じく、移動のたびに並べ替えるとツリーが目の前で動いてしまうため、
    /// 起動時とノードの展開時にだけ呼ぶ（ノードを開いた先のファイル一覧は常に最新の順で作られる）。
    /// </summary>
    public void RefreshRecentFolders()
    {
        ReplaceShortcutNodes(_recentFolders, _shell.GetRecentPaths());
    }

    /// <summary>複製ノードの一覧を、指定のパス群に合わせて差し替える（並びが同じなら何もしない）。</summary>
    private void ReplaceShortcutNodes(ObservableCollection<FolderItemViewModel> nodes, IReadOnlyList<string> paths)
    {
        // 並びが変わっていなければ作り直さない（展開状態を保つ）
        if (paths.Count == nodes.Count
            && paths.Zip(nodes).All(pair => string.Equals(pair.First, pair.Second.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        nodes.Clear();
        foreach (var path in paths)
        {
            nodes.Add(CreateShortcutNode(path));
        }
    }

    /// <summary>お気に入り／最近／よく使う配下に置く、実体ツリーの複製ノードを生成する。</summary>
    private FolderItemViewModel CreateShortcutNode(string path)
    {
        return new FolderItemViewModel(path, _shell.IsExcludedPath, isShortcut: true);
    }

    /// <summary>ルートフォルダのノードを、指定のパス群へ差分更新する。</summary>
    internal void ApplyRootPaths(IReadOnlyList<string> rootPaths)
    {
        var newRootPaths = new HashSet<string>(rootPaths, StringComparer.OrdinalIgnoreCase);

        // 削除: 新しいリストに含まれないルートフォルダを削除
        var rootsToRemove = RootFolders
            .Where(x => !newRootPaths.Contains(x.Path))
            .ToList();
        foreach (var root in rootsToRemove)
        {
            RootFolders.Remove(root);
        }

        // 追加: 新しいリストに含まれるがまだ存在しないルートフォルダを追加
        var existingRootPaths = new HashSet<string>(RootFolders.Select(x => x.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var rootPath in rootPaths)
        {
            if (!existingRootPaths.Contains(rootPath))
            {
                var newRootFolder = new FolderItemViewModel(rootPath, _shell.IsExcludedPath);
                // ルートフォルダは追加時に即座に読み込みを開始する（遅延展開ではなく）。
                // ただし同期版だと切断中のNASルートでUIスレッドがSMBタイムアウトまでブロックするため、
                // 非同期版で開始だけして先へ進む（読み込み完了までツリーにはダミーの子が表示される）
                _ = newRootFolder.EnsureLoadedAsync();
                RootFolders.Add(newRootFolder);
            }
        }

        // 並び替え: 既存項目の削除・追加だけでは順序変更が反映されないため、設定の順序に合わせて移動する。
        // 再生成せずMoveで並び替えることで、読み込み済みのサブフォルダツリーを保持する
        var orderedIndex = 0;
        foreach (var rootPath in rootPaths)
        {
            var currentIndex = -1;
            for (var i = 0; i < RootFolders.Count; i++)
            {
                if (string.Equals(RootFolders[i].Path, rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                continue;
            }

            if (currentIndex != orderedIndex)
            {
                RootFolders.Move(currentIndex, orderedIndex);
            }

            orderedIndex++;
        }
    }

    /// <summary>
    /// 読み込み済みの子フォルダを捨てて読み直す（隠し/システム属性の表示設定が変わったとき）。
    /// お気に入り等の複製ノードも同じ実フォルダを列挙するため対象に含める。
    /// </summary>
    internal void ReloadTreeFolders()
    {
        foreach (var folder in RootFolders.Concat(_favoriteFolders).Concat(_frequentFolders).Concat(_recentFolders))
        {
            folder.Reload();
        }
    }

    /// <summary>
    /// 言語切り替え後に、ツリー上の言語依存の表示名（仮想ノード・遅延読み込み中のダミー）を引き直す。
    /// TreeRootsに出ていない（未購読時の）お気に入り・最近・よく使うノードも含めて更新する。
    /// </summary>
    internal void RefreshLocalizedTreeNames()
    {
        AllRootsNode.RefreshLocalizedDisplayName();
        _favoritesNode.RefreshLocalizedDisplayName();
        _frequentNode.RefreshLocalizedDisplayName();
        _recentNode.RefreshLocalizedDisplayName();
    }
}
