using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ParallelScope.ViewModels;

namespace ParallelScope.Views;

/// <summary>
/// タブ列の操作（追加・複製・クローズ・切り替え・ドラッグでの並べ替え）に関する処理。
/// タブはPlus機能のため、未購読の間はタブ列を出さず操作も受け付けない。
/// </summary>
public partial class BrowserPaneView
{
    // ドラッグ開始の判定に使う、押した位置と押されたタブ
    private Point _tabDragStartPoint;
    private BrowserTabViewModel? _tabDragCandidate;

    // タブ機能（タブ列の表示とタブ操作）が使えるか。未購読の間はfalse
    private bool _areTabsEnabled;

    /// <summary>タブ機能の有効/無効を購読状態に合わせて切り替える。</summary>
    internal void SetTabsEnabled(bool isEnabled)
    {
        _areTabsEnabled = isEnabled;
        TabStripBorder.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>タブ機能が現在使えるか（ウィンドウ側のショートカット処理から参照する）。</summary>
    internal bool AreTabsEnabled => _areTabsEnabled;

    /// <summary>新しいタブを開く（現在のタブと同じ場所）。</summary>
    internal void OpenNewTab()
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.OpenTab();
        }
    }

    /// <summary>指定パスを新しいタブで開く。</summary>
    internal void OpenPathInNewTab(string path)
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.OpenTab(path);
        }
    }

    /// <summary>表示中のタブを閉じる。</summary>
    internal void CloseActiveTab()
    {
        if (_areTabsEnabled)
        {
            CloseTab(ActiveTab);
        }
    }

    /// <summary>
    /// タブを閉じる。2画面で最後の1つを閉じたときは、そのペインごと閉じて1画面に戻す
    /// （1画面のときは従来どおり最後の1つを閉じられない）。
    /// </summary>
    private void CloseTab(BrowserTabViewModel tab)
    {
        if (_paneViewModel.CanCloseTabs)
        {
            _paneViewModel.CloseTab(tab);
            return;
        }

        // 最後の1つ。閉じるタブは残る側へ移さず捨てる（このタブを閉じる操作なので）
        _host.ClosePane(_paneViewModel, moveTabs: false);
    }

    /// <summary>直前に閉じたタブを開き直す。</summary>
    internal void ReopenClosedTab()
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.ReopenClosedTab();
        }
    }

    /// <summary>隣のタブへ移る（端まで来たら反対の端へ回り込む）。</summary>
    internal void ActivateAdjacentTab(bool forward)
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.ActivateAdjacentTab(forward);
        }
    }

    /// <summary>左から数えて指定位置のタブを表示する。</summary>
    internal void ActivateTabAt(int index)
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.ActivateTabAt(index);
        }
    }

    /// <summary>末尾のタブを表示する。</summary>
    internal void ActivateLastTab()
    {
        if (_areTabsEnabled)
        {
            _paneViewModel.ActivateLastTab();
        }
    }

    // 表示中のタブが切り替わったら、タブごとに持っている表示状態（ソート順・ツリー選択）を反映し直す
    private void PaneViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BrowserPaneViewModel.ActiveTab))
        {
            return;
        }

        SyncTreeSelectionToCurrentPath();

        // ItemsSource のバインディングが新しいタブの一覧へ差し替わってからソートを適用する
        Dispatcher.InvokeAsync(ApplyActiveTabSort, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => OpenNewTab();

    private void NewTabMenuItem_Click(object sender, RoutedEventArgs e) => OpenNewTab();

    private void DuplicateTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTabFrom(sender) is { } tab)
        {
            _paneViewModel.DuplicateTab(tab);
        }
    }

    private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTabFrom(sender) is { } tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseOtherTabsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetTabFrom(sender) is { } tab)
        {
            _paneViewModel.CloseOtherTabs(tab);
        }
    }

    private void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetTabFrom(sender) is { } tab)
        {
            CloseTab(tab);
        }
    }

    // 左クリックでタブを表示、中クリックでタブを閉じる。左ボタンはドラッグ開始の候補としても控える
    private void TabItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (GetTabFrom(sender) is not { } tab)
        {
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            CloseTab(tab);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _tabDragStartPoint = e.GetPosition(this);
        _tabDragCandidate = tab;
        _paneViewModel.ActivateTab(tab);
    }

    // 押したまま一定距離動かしたらタブのドラッグを始める（クリックとの誤爆を防ぐ）
    private void TabItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _tabDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _tabDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _tabDragCandidate;
        _tabDragCandidate = null;

        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);
        }
        finally
        {
            ClearTabDropIndicators();
        }
    }

    private void TabItem_DragOver(object sender, DragEventArgs e)
    {
        if (GetDraggedTab(e) is not { } dragged || GetTabFrom(sender) is not { } target)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!CanAcceptDraggedTab(dragged))
        {
            // 移動元が空になる場合と、このペインが上限に達している場合は受け付けない
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ClearTabDropIndicators();
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        ClearTabDropIndicators();

        if (ReferenceEquals(dragged, target))
        {
            return;
        }

        // カーソルがタブの左半分なら手前、右半分なら後ろへ落とす
        if (IsBeforeHalf(sender, e))
        {
            target.IsDropTargetBefore = true;
        }
        else
        {
            target.IsDropTargetAfter = true;
        }
    }

    private void TabItem_DragLeave(object sender, DragEventArgs e)
    {
        ClearTabDropIndicators();
    }

    private void TabItem_Drop(object sender, DragEventArgs e)
    {
        ClearTabDropIndicators();

        if (GetDraggedTab(e) is not { } dragged || GetTabFrom(sender) is not { } target)
        {
            return;
        }

        e.Handled = true;

        if (ReferenceEquals(dragged, target))
        {
            return;
        }

        var targetIndex = _paneViewModel.Tabs.IndexOf(target);
        if (targetIndex < 0)
        {
            return;
        }

        var newIndex = IsBeforeHalf(sender, e) ? targetIndex : targetIndex + 1;

        // 別のペインから運ばれてきたタブは、インスタンスをそのまま受け取る
        if (!_paneViewModel.Tabs.Contains(dragged))
        {
            _viewModel.MoveTabToPane(dragged, _paneViewModel, newIndex);
            return;
        }

        // 自分より後ろへ入れる場合、抜いた分だけ位置が1つ詰まる
        if (_paneViewModel.Tabs.IndexOf(dragged) < newIndex)
        {
            newIndex--;
        }

        _paneViewModel.MoveTab(dragged, newIndex);
    }

    // タブ列の余白（タブが並んでいない部分）へ落とされた場合は末尾へ移す
    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        var dragged = GetDraggedTab(e);
        e.Effects = dragged is not null && CanAcceptDraggedTab(dragged)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        ClearTabDropIndicators();

        if (GetDraggedTab(e) is not { } dragged)
        {
            return;
        }

        e.Handled = true;

        if (_paneViewModel.Tabs.Contains(dragged))
        {
            _paneViewModel.MoveTab(dragged, _paneViewModel.Tabs.Count - 1);
            return;
        }

        _viewModel.MoveTabToPane(dragged, _paneViewModel, _paneViewModel.Tabs.Count);
    }

    /// <summary>ドラッグ中のタブをこのペインで受け取れるか（同じペイン内の並べ替えは常に可）。</summary>
    private bool CanAcceptDraggedTab(BrowserTabViewModel dragged)
    {
        if (_paneViewModel.Tabs.Contains(dragged))
        {
            return true;
        }

        // 移動元の最後の1つは持ち出せない。移動先が上限に達している場合も受け取らない
        return _paneViewModel.CanAddTab
            && _viewModel.FindPaneOf(dragged) is { CanCloseTabs: true };
    }

    private static bool IsBeforeHalf(object sender, DragEventArgs e)
    {
        var element = (FrameworkElement)sender;
        return e.GetPosition(element).X < element.ActualWidth / 2;
    }

    private static BrowserTabViewModel? GetDraggedTab(DragEventArgs e)
    {
        return e.Data.GetDataPresent(typeof(BrowserTabViewModel))
            ? e.Data.GetData(typeof(BrowserTabViewModel)) as BrowserTabViewModel
            : null;
    }

    private void ClearTabDropIndicators()
    {
        foreach (var tab in _paneViewModel.Tabs)
        {
            tab.IsDropTargetBefore = false;
            tab.IsDropTargetAfter = false;
        }
    }

    private static BrowserTabViewModel? GetTabFrom(object sender)
    {
        return (sender as FrameworkElement)?.DataContext as BrowserTabViewModel;
    }
}
