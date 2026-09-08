using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ParallelScope.Utilities;
using ParallelScope.ViewModels;

namespace ParallelScope.Views;

/// <summary>フォルダツリーの操作（選択の同期・展開・コンテキストメニュー）に関する処理。</summary>
public partial class BrowserPaneView
{
    // 右クリックで押されたツリーノード（マウスを離す時点でカーソル直下が変わっても対象を保つため）
    private TreeViewItem? _rightClickedTreeViewItem;

    // パス→TreeViewItem の対応表。ツリーはペインごとに別インスタンスなので、この表もペインごとに持つ
    private readonly Dictionary<string, TreeViewItem> _treeItemMap = new(StringComparer.OrdinalIgnoreCase);

    // ツリーの開閉が使えるか。未購読の間はfalse（開いたまま固定）
    private bool _isTreeCollapseEnabled;

    // 畳む直前のツリー幅。開き直したときに元の幅へ戻すために控える
    private GridLength _expandedTreeWidth = new(200);

    /// <summary>ツリーの開閉（Plus機能）の有効/無効を購読状態に合わせて切り替える。</summary>
    internal void SetTreeCollapseEnabled(bool isEnabled)
    {
        _isTreeCollapseEnabled = isEnabled;
        TreeToggleButton.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        ApplyTreeVisibility();
    }

    /// <summary>ツリーの開閉状態を画面へ反映する（未購読の間は畳まず常に開く）。</summary>
    internal void ApplyTreeVisibility()
    {
        var isVisible = !_isTreeCollapseEnabled || _paneViewModel.IsTreeVisible;
        if (isVisible == (TreeBorder.Visibility == Visibility.Visible))
        {
            return;
        }

        if (isVisible)
        {
            TreeColumn.MinWidth = 120;
            TreeColumn.Width = _expandedTreeWidth;
        }
        else
        {
            // 幅の下限を残したままだと列が縮みきらないため、畳む間だけ0にする
            _expandedTreeWidth = new GridLength(TreeColumn.ActualWidth > 0 ? TreeColumn.ActualWidth : 200);
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(0);
        }

        TreeBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        TreeSplitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    // 生成されたTreeViewItemをパスで引けるように記録する（ツリー選択の同期に使用）
    private void FolderTreeViewItem_Loaded(object sender, RoutedEventArgs e)
    {
        // お気に入り／よく使い配下は実体ツリーの複製なので登録しない
        // （同じパスで上書きされると、実体ツリーで選択したつもりが複製側へ飛んでしまう）
        if (sender is TreeViewItem tvi && tvi.DataContext is FolderItemViewModel { IsShortcut: false } vm)
        {
            _treeItemMap[vm.Path] = tvi;
        }
    }

    // ツリーから外れたTreeViewItem（設定変更でのルート再構築・子の再読み込みで破棄されたもの）への
    // 参照をマップに残さない（残すと配下のビジュアルツリーごと解放されず、メモリが増え続ける）。
    // 同一パスに新しいインスタンスが登録済みの場合は消さない（Loaded→古い方のUnloadedの順で届くことがあるため）
    private void FolderTreeViewItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem tvi)
        {
            return;
        }

        if (tvi.DataContext is FolderItemViewModel vm)
        {
            if (_treeItemMap.TryGetValue(vm.Path, out var mapped) && ReferenceEquals(mapped, tvi))
            {
                _treeItemMap.Remove(vm.Path);
            }

            return;
        }

        // DataContextが既に外れている場合は、値側から一致するエントリを探して除去する
        foreach (var pair in _treeItemMap)
        {
            if (ReferenceEquals(pair.Value, tvi))
            {
                _treeItemMap.Remove(pair.Key);
                return;
            }
        }
    }

    // ツリーで選択されたフォルダのファイル一覧を読み込む
    private void FolderTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderItemViewModel folderItem)
        {
            ActiveTab.LoadFiles(folderItem.Path);
        }
    }

    private async void FolderTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        // 「最近」「よく使う」は展開のタイミングでだけ並べ直す（移動のたびに並べ替えるとツリーが目の前で動いてしまう）
        switch (VirtualFolders.GetKind(folderItem.Path))
        {
            case VirtualFolderKind.Frequent:
                _paneViewModel.RefreshFrequentFolders();
                return;
            case VirtualFolderKind.Recent:
                _paneViewModel.RefreshRecentFolders();
                return;
        }

        // TreeViewItemが展開される時に、子フォルダを遅延読み込み（非同期）
        await folderItem.EnsureLoadedAsync();
    }

    // 右クリックされたTreeViewItemを選択状態にしてからコンテキストメニューを表示する
    private void FolderTreeView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var treeViewItem = GetAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (treeViewItem is null)
        {
            return;
        }

        // 選択・フォーカスでツリーがスクロールすると、マウスを離す時点のカーソル直下が
        // 別ノード（あるいは余白）になりうる。メニューの対象は押した時点のノードで固定する
        _rightClickedTreeViewItem = treeViewItem;

        treeViewItem.IsSelected = true;
        treeViewItem.Focus();
    }

    // 選択中のフォルダに対するコンテキストメニューを動的に構築して開く
    private void FolderTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // 組み立てたメニューは自前で開く。WPFの自動表示に任せると、
        // この時点ではまだ割り当てられていないため「1回目は出ず2回目で出る」ことがある
        e.Handled = true;

        // キーボード（アプリケーションキー）からの表示ではカーソル位置が-1で、直前の右クリック位置は無関係
        var isKeyboardInvoked = e.CursorLeft < 0 && e.CursorTop < 0;
        var treeViewItem = isKeyboardInvoked
            ? GetAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)
            : _rightClickedTreeViewItem;
        _rightClickedTreeViewItem = null;

        if (treeViewItem is not { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        // 仮想ノード（Folders / Favorites / Recent / Frequently Used）は実パスを持たず個別スキャンできないため、メニューを表示しない
        if (VirtualFolders.IsVirtual(folderItem.Path))
        {
            treeViewItem.ContextMenu = null;
            return;
        }

        var scanMenuItem = new MenuItem
        {
            Header = UiText.Get("Context.ScanSubtree"),
            DataContext = folderItem,
            IsEnabled = !folderItem.IsScanning
        };
        scanMenuItem.Click += ScanFolderMenuItem_Click;

        var contextMenu = new ContextMenu
        {
            DataContext = folderItem,
            PlacementTarget = treeViewItem
        };

        // タブはPlus機能のため、未購読の間はメニューにも出さない
        if (_areTabsEnabled)
        {
            var openInNewTabMenuItem = new MenuItem
            {
                Header = UiText.Get("Context.OpenInNewTab"),
                DataContext = folderItem,
                IsEnabled = _paneViewModel.CanAddTab
            };
            openInNewTabMenuItem.Click += OpenFolderInNewTabMenuItem_Click;

            contextMenu.Items.Add(openInNewTabMenuItem);

            // 1画面のときは反対側のペインが無いが、その場合はクリック時に分割して開くので選べる状態にする
            var otherPane = _viewModel.GetOtherPane(_paneViewModel);
            var openInOtherPaneMenuItem = new MenuItem
            {
                Header = UiText.Get("Context.OpenInOtherPane"),
                DataContext = folderItem,
                IsEnabled = otherPane?.CanAddTab ?? true
            };
            openInOtherPaneMenuItem.Click += OpenFolderInOtherPaneMenuItem_Click;
            contextMenu.Items.Add(openInOtherPaneMenuItem);

            contextMenu.Items.Add(new Separator());
        }

        contextMenu.Items.Add(scanMenuItem);

        // お気に入りはPlus機能のため、未購読の間はメニューにも出さない
        if (_viewModel.ArePlusFeaturesEnabled)
        {
            var isFavorite = _viewModel.IsFavorite(folderItem.Path);
            var favoriteMenuItem = new MenuItem
            {
                Header = UiText.Get(isFavorite ? "Context.RemoveFavorite" : "Context.AddFavorite"),
                DataContext = folderItem
            };
            favoriteMenuItem.Click += ToggleFavoriteMenuItem_Click;

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(favoriteMenuItem);
        }

        // 「このペインを閉じる」は2画面のときしか意味が無いので、1画面ではメニューに出さない
        if (_viewModel.IsSplitViewEnabled)
        {
            var closePaneMenuItem = new MenuItem { Header = UiText.Get("Context.ClosePane") };
            closePaneMenuItem.Click += ClosePaneMenuItem_Click;

            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(closePaneMenuItem);
        }

        if (isKeyboardInvoked)
        {
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }

        // テーマ・言語のリソースを引けるよう、開く前に論理ツリーへ繋いでおく
        treeViewItem.ContextMenu = contextMenu;
        contextMenu.IsOpen = true;
    }

    // コンテキストメニューから、選択フォルダ配下の個別スキャンを要求する
    // （スキャンはアプリ全体で1本に統合されているため、実行はウィンドウ側に委ねる）
    private async void ScanFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            return;
        }

        await _host.RunFolderScanAsync(folderItem);
    }

    // ツリーのコンテキストメニューから、選択フォルダを新しいタブで開く
    private void OpenFolderInNewTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            OpenPathInNewTab(folderItem.Path);
        }
    }

    // ツリーのコンテキストメニューから、選択フォルダを反対側のペインで開く
    private void OpenFolderInOtherPaneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            OpenPathInOtherPane(folderItem.Path);
        }
    }

    // コンテキストメニューから、選択フォルダのお気に入り登録/解除を切り替える
    private void ToggleFavoriteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FolderItemViewModel folderItem })
        {
            _viewModel.ToggleFavorite(folderItem.Path);
        }
    }

    // 指定アイテムの祖先TreeViewItemをすべて展開する
    private void ExpandParents(TreeViewItem item)
    {
        // 展開するアイテムをすべて収集してからバッチで展開
        var itemsToExpand = new List<TreeViewItem>();
        DependencyObject parent = VisualTreeHelper.GetParent(item);

        while (parent is TreeViewItem parentItem)
        {
            itemsToExpand.Add(parentItem);
            parent = VisualTreeHelper.GetParent(parentItem);
        }

        // バッチ展開（複数の IsExpanded 設定をまとめる）
        foreach (var parentItem in itemsToExpand)
        {
            parentItem.IsExpanded = true;
        }

        // 最後に一度だけレイアウト更新
        if (itemsToExpand.Count > 0)
        {
            item.UpdateLayout();
        }
    }

    // ツリー選択の同期は、子フォルダの読み込みを待つ間に別のフォルダへ移動されうる。
    // 割り込まれた古い同期が選択を書き戻さないよう、要求ごとに番号を振って最新のものだけを通す
    private int _treeSyncVersion;

    private bool IsCurrentTreeSync(int version) => version == _treeSyncVersion;

    // フォルダツリーの選択状態を現在のパスに同期する（必要に応じて祖先ノードを遅延展開）。
    // 結果を待つ呼び出し元は無いため投げっぱなしにする
    internal void SyncTreeSelectionToCurrentPath()
    {
        _ = SyncTreeSelectionToCurrentPathAsync();
    }

    private async Task SyncTreeSelectionToCurrentPathAsync()
    {
        var version = ++_treeSyncVersion;

        var path = PathNormalizer.Normalize(ActiveTab.CurrentPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_treeItemMap.TryGetValue(path, out var tvi))
        {
            ExpandParents(tvi);
            tvi.IsSelected = true;
            tvi.BringIntoView();
            return;
        }

        var rootFolder = _paneViewModel.RootFolders.FirstOrDefault(root => PathNormalizer.IsAncestorOrSame(root.Path, path));
        if (rootFolder is null)
        {
            return;
        }

        var normalizedRootPath = PathNormalizer.Normalize(rootFolder.Path);
        var relativePath = path.StartsWith(normalizedRootPath, StringComparison.OrdinalIgnoreCase)
            ? path[normalizedRootPath.Length..]
            : string.Empty;

        var pathComponents = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // ルートは仮想「Folders」ノードの子になったため、そのTreeViewItemを展開してから配下を辿る
        // （Favorites/Frequently Usedが上に挿入されうるので、インデックスではなくノード実体から引く）
        if (FolderTreeView.ItemContainerGenerator.ContainerFromItem(_paneViewModel.AllRootsNode) is not TreeViewItem allRootsItem)
        {
            return;
        }

        allRootsItem.IsExpanded = true;

        await ExpandAndSelectByPathAsync(allRootsItem, rootFolder, pathComponents, 0, version);

        if (!IsCurrentTreeSync(version))
        {
            return;
        }

        if (_treeItemMap.TryGetValue(path, out tvi))
        {
            ExpandParents(tvi);
        }
    }

    // パス構成要素を1つずつ辿りながらツリーを再帰的に展開し、目的のノードを選択する
    private async Task<bool> ExpandAndSelectByPathAsync(ItemsControl parentControl, FolderItemViewModel folderItem,
        List<string> pathComponents, int componentIndex, int version)
    {
        // 初回呼び出しのみレイアウト更新を行う
        if (componentIndex == 0)
        {
            parentControl.UpdateLayout();
        }

        if (parentControl.ItemContainerGenerator.ContainerFromItem(folderItem) is not TreeViewItem treeViewItem)
        {
            return false;
        }

        // ターゲットに到達した
        if (componentIndex >= pathComponents.Count)
        {
            treeViewItem.IsSelected = true;
            treeViewItem.BringIntoView();
            // 最終更新のみ一度実行
            treeViewItem.UpdateLayout();
            return true;
        }

        // 遅延読み込みを実行（次のディレクトリを探すために）。
        // 列挙はバックグラウンドで行われるので、ここで待ってもUIスレッドは止まらない
        await folderItem.EnsureLoadedAsync();

        // 待っている間に別のフォルダへ移動していれば、そちらの同期に任せて降りる
        if (!IsCurrentTreeSync(version))
        {
            return false;
        }

        treeViewItem.IsExpanded = true;
        // 中間のUpdateLayout()は削除（最後の更新のみで十分）

        // 次のディレクトリ成分を探す
        var nextComponent = pathComponents[componentIndex];
        var matchingChild = folderItem.SubFolders.FirstOrDefault(child =>
            string.Equals(child.DisplayName, nextComponent, StringComparison.OrdinalIgnoreCase));

        if (matchingChild is not null)
        {
            return await ExpandAndSelectByPathAsync(treeViewItem, matchingChild, pathComponents, componentIndex + 1, version);
        }

        return false;
    }
}
